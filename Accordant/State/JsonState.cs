// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Accordant
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;
    using System.Threading;

    /// <summary>
    /// A simplified base class for state objects that uses JSON serialization
    /// for cloning, fingerprinting, and string representation.
    /// 
    /// This class is designed for ease of use - users simply define their state
    /// as a class with public properties (getter and setter), and the framework
    /// handles cloning, locking, and fingerprinting automatically.
    /// 
    /// Supports:
    /// - Cyclic references between JsonState objects
    /// - Nested JsonState objects
    /// - Collections (List, Dictionary) containing primitives or JsonState objects
    /// - Reference preservation (same object referenced twice stays same after clone)
    /// 
    /// Requirements:
    /// - All state properties must have public getter AND setter
    /// - Subclasses must have a parameterless constructor
    /// - Property types must be JSON-serializable
    /// </summary>
    public abstract class JsonState : State
    {
        /// <summary>
        /// Thread-local storage for mutation detection flag.
        /// Uses AsyncLocal to properly flow across async/await boundaries.
        /// </summary>
        private static readonly AsyncLocal<bool?> _enableMutationDetectionLocal = new AsyncLocal<bool?>();

        /// <summary>
        /// When enabled, the framework will detect if a JsonState was mutated after
        /// being locked and throw a <see cref="StateLockedException"/>.
        /// This helps catch bugs where users forget to clone before modifying.
        /// Default is true.
        /// 
        /// This property is thread-safe and uses AsyncLocal, so each async execution
        /// context can have its own value. When not explicitly set in the current context,
        /// defaults to true.
        /// </summary>
        public static bool EnableMutationDetection
        {
            get => _enableMutationDetectionLocal.Value ?? true;
            set => _enableMutationDetectionLocal.Value = value;
        }

        /// <summary>
        /// Snapshot of the JSON representation when the state was locked.
        /// Used for mutation detection.
        /// </summary>
        private string _lockedSnapshot;

        /// <summary>
        /// Creates JsonSerializerOptions for cloning (excludes atomic properties).
        /// </summary>
        private static JsonSerializerOptions CreateCloneOptions()
        {
            var referenceHandler = new PreserveReferenceHandler();
            
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = referenceHandler,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                TypeInfoResolver = new AtomicExcludingTypeInfoResolver(includeFingerprintProperties: false)
            };

            options.Converters.Add(new SortedDictionaryConverterFactory());
            return options;
        }

        /// <summary>
        /// Creates JsonSerializerOptions for fingerprinting (excludes atomic properties, includes fingerprint properties).
        /// </summary>
        private static JsonSerializerOptions CreateFingerprintOptions()
        {
            var referenceHandler = new PreserveReferenceHandler();
            
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = referenceHandler,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                TypeInfoResolver = new AtomicExcludingTypeInfoResolver(includeFingerprintProperties: true)
            };

            options.Converters.Add(new SortedDictionaryConverterFactory());
            return options;
        }

        /// <summary>
        /// Creates JsonSerializerOptions with a fresh ReferenceHandler for each operation.
        /// This ensures reference tracking is maintained across the entire object graph
        /// including through custom converters.
        /// </summary>
        private static JsonSerializerOptions CreateOptionsWithFreshReferenceHandler()
        {
            var referenceHandler = new PreserveReferenceHandler();
            
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = referenceHandler,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            };

            // Add converter for deterministic dictionary key ordering
            options.Converters.Add(new SortedDictionaryConverterFactory());

            return options;
        }

        /// <summary>
        /// Recursively copies atomic property values from source to target.
        /// Walks the entire object graph following JsonState objects.
        /// </summary>
        private static void CopyAtomicPropertiesRecursive(
            object source, 
            object target, 
            HashSet<object> visited)
        {
            if (source == null || target == null)
                return;

            // Avoid cycles
            if (!visited.Add(source))
                return;

            var type = source.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                // Check if this property is atomic
                var atomicAttr = prop.GetCustomAttribute<JsonAtomicAttribute>();
                if (atomicAttr != null)
                {
                    // Copy reference from source to target
                    var value = prop.GetValue(source);
                    prop.SetValue(target, value);
                    continue;
                }

                // Check if property has both getter and setter
                var getter = prop.GetMethod;
                var setter = prop.SetMethod;
                if (getter == null || !getter.IsPublic || setter == null || !setter.IsPublic)
                    continue;

                // Recursively process nested JsonState objects
                var sourceValue = prop.GetValue(source);
                var targetValue = prop.GetValue(target);

                if (sourceValue is JsonState && targetValue is JsonState)
                {
                    CopyAtomicPropertiesRecursive(sourceValue, targetValue, visited);
                }
                else if (sourceValue is IEnumerable sourceEnum && targetValue is IEnumerable targetEnum 
                         && !(sourceValue is string))
                {
                    // Handle collections - pair up items by index/key and recurse
                    CopyAtomicPropertiesInCollection(sourceEnum, targetEnum, visited);
                }
            }
        }

        /// <summary>
        /// Copies atomic properties within collection elements.
        /// </summary>
        private static void CopyAtomicPropertiesInCollection(
            IEnumerable source, 
            IEnumerable target, 
            HashSet<object> visited)
        {
            if (source is IDictionary sourceDict && target is IDictionary targetDict)
            {
                foreach (var key in sourceDict.Keys)
                {
                    if (targetDict.Contains(key))
                    {
                        var sourceVal = sourceDict[key];
                        var targetVal = targetDict[key];
                        if (sourceVal is JsonState && targetVal is JsonState)
                        {
                            CopyAtomicPropertiesRecursive(sourceVal, targetVal, visited);
                        }
                    }
                }
            }
            else
            {
                var sourceList = source.Cast<object>().ToList();
                var targetList = target.Cast<object>().ToList();

                for (int i = 0; i < Math.Min(sourceList.Count, targetList.Count); i++)
                {
                    if (sourceList[i] is JsonState && targetList[i] is JsonState)
                    {
                        CopyAtomicPropertiesRecursive(sourceList[i], targetList[i], visited);
                    }
                }
            }
        }

        /// <summary>
        /// Core cloning logic via JSON serialization/deserialization.
        /// Returns the cloned object without populating any map.
        /// </summary>
        private JsonState CloneViaJson()
        {
            try
            {
                var options = CreateCloneOptions();
                var json = JsonSerializer.Serialize(this, GetType(), options);
                var clone = (JsonState)JsonSerializer.Deserialize(json, GetType(), options);

                // Recursively copy atomic property references from original to clone
                CopyAtomicPropertiesRecursive(this, clone, new HashSet<object>(ReferenceEqualityComparer.Instance));

                return clone;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to clone JsonState of type {GetType().Name}. " +
                    "Ensure all properties are JSON-serializable and the class has a parameterless constructor.",
                    ex);
            }
        }

        /// <summary>
        /// Optimized clone that skips map population since the map will be discarded.
        /// </summary>
        public override State Clone()
        {
            return CloneViaJson();
        }

        /// <summary>
        /// Clones this state by serializing to JSON and deserializing back.
        /// The cloned state is unlocked and independent of the original.
        /// Also populates clonedMap with all nested JsonState pairs.
        /// 
        /// Properties marked with [JsonAtomic] are excluded from serialization
        /// and copied by reference (shallow copy) after deserialization.
        /// </summary>
        protected override void CloneInternal(Dictionary<object, object> clonedMap)
        {
            var clone = CloneViaJson();
            PopulateClonedMap(this, clone, clonedMap, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        /// <summary>
        /// Walks the original and cloned object graphs in parallel,
        /// populating clonedMap with all nested JsonState pairs.
        /// </summary>
        private static void PopulateClonedMap(
            object original,
            object clone,
            Dictionary<object, object> clonedMap,
            HashSet<object> visited)
        {
            if (original == null || clone == null)
                return;

            // Avoid cycles using original's identity
            if (!visited.Add(original))
                return;

            // Add JsonState pairs to the map
            if (original is JsonState originalState && clone is JsonState cloneState)
            {
                clonedMap[originalState] = cloneState;
            }

            var type = original.GetType();
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                if (!HasPublicGetterAndSetter(prop))
                    continue;

                try
                {
                    var originalValue = prop.GetValue(original);
                    var cloneValue = prop.GetValue(clone);

                    if (originalValue is JsonState && cloneValue is JsonState)
                    {
                        PopulateClonedMap(originalValue, cloneValue, clonedMap, visited);
                    }
                    else if (originalValue is IEnumerable origEnum &&
                             cloneValue is IEnumerable cloneEnum &&
                             !(originalValue is string))
                    {
                        PopulateClonedMapInCollection(origEnum, cloneEnum, clonedMap, visited);
                    }
                }
                catch (TargetInvocationException)
                {
                    // Property getter threw - skip this property
                }
            }
        }

        /// <summary>
        /// Populates clonedMap with JsonState pairs found in collections.
        /// </summary>
        private static void PopulateClonedMapInCollection(
            IEnumerable original,
            IEnumerable clone,
            Dictionary<object, object> clonedMap,
            HashSet<object> visited)
        {
            if (original is IDictionary origDict && clone is IDictionary cloneDict)
            {
                foreach (var key in origDict.Keys)
                {
                    if (cloneDict.Contains(key))
                    {
                        var origVal = origDict[key];
                        var cloneVal = cloneDict[key];
                        if (origVal is JsonState && cloneVal is JsonState)
                        {
                            PopulateClonedMap(origVal, cloneVal, clonedMap, visited);
                        }
                    }
                }
            }
            else
            {
                var origList = original.Cast<object>().ToList();
                var cloneList = clone.Cast<object>().ToList();

                for (int i = 0; i < Math.Min(origList.Count, cloneList.Count); i++)
                {
                    if (origList[i] is JsonState && cloneList[i] is JsonState)
                    {
                        PopulateClonedMap(origList[i], cloneList[i], clonedMap, visited);
                    }
                }
            }
        }

        /// <summary>
        /// Returns a string representation based on the type name and JSON serialization.
        /// This provides a unique fingerprint for the state.
        /// 
        /// Properties marked with [JsonAtomic] are excluded from serialization.
        /// Their fingerprint is provided by the property specified in the attribute.
        /// </summary>
        protected override string StringRepresentationInternal(
            Dictionary<object, string> objectPaths,
            string path)
        {
            // We ignore objectPaths/path because JSON handles cycles via $ref
            try
            {
                var options = CreateFingerprintOptions();
                var json = JsonSerializer.Serialize(this, GetType(), options);
                return GetType().Name + " " + json;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to serialize JsonState of type {GetType().Name} for string representation. " +
                    "Ensure all properties are JSON-serializable.",
                    ex);
            }
        }

        /// <summary>
        /// Recursively locks all nested State objects found in public properties.
        /// Only considers properties with both public getter and setter.
        /// Also captures a snapshot for mutation detection if enabled.
        /// </summary>
        protected override void LockComponents(HashSet<object> visited)
        {
            // Capture snapshot BEFORE locking (when state is still fully readable)
            // Atomic properties are excluded (they're immutable by contract)
            // Fingerprint properties are included for proper mutation detection
            if (EnableMutationDetection && _lockedSnapshot == null)
            {
                try
                {
                    var options = CreateFingerprintOptions();
                    _lockedSnapshot = JsonSerializer.Serialize(this, GetType(), options);
                }
                catch (JsonException)
                {
                    // If serialization fails, we can't do mutation detection
                    _lockedSnapshot = null;
                }
            }

            var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                // Only consider properties with both public getter and setter
                if (!HasPublicGetterAndSetter(prop))
                {
                    continue;
                }

                try
                {
                    var value = prop.GetValue(this);
                    LockValue(value, visited);
                }
                catch (TargetInvocationException)
                {
                    // Property getter threw - skip this property
                }
            }
        }

        /// <summary>
        /// Checks if a property has both a public getter and a public setter.
        /// </summary>
        private static bool HasPublicGetterAndSetter(PropertyInfo prop)
        {
            var getter = prop.GetMethod;
            var setter = prop.SetMethod;

            return getter != null && getter.IsPublic &&
                   setter != null && setter.IsPublic;
        }

        /// <summary>
        /// Locks a value if it's a State, or recursively processes collections.
        /// </summary>
        private static void LockValue(object value, HashSet<object> visited)
        {
            if (value == null)
            {
                return;
            }

            if (value is State stateValue)
            {
                stateValue.Lock(visited);
            }
            else if (value is IDictionary dictionary)
            {
                foreach (var dictValue in dictionary.Values)
                {
                    LockValue(dictValue, visited);
                }
            }
            else if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    LockValue(item, visited);
                }
            }
        }

        /// <summary>
        /// Validates that the state has not been mutated since it was locked.
        /// Called by the framework after user code runs.
        /// Throws <see cref="StateLockedException"/> if mutation is detected.
        /// 
        /// Note: Atomic properties are excluded from mutation detection
        /// (they're immutable by contract). Fingerprint properties are included.
        /// </summary>
        public void ValidateNotMutated()
        {
            if (!EnableMutationDetection || !Locked || _lockedSnapshot == null)
            {
                return;
            }

            try
            {
                var options = CreateFingerprintOptions();
                var currentJson = JsonSerializer.Serialize(this, GetType(), options);
                if (currentJson != _lockedSnapshot)
                {
                    throw new StateLockedException(
                        $"JsonState of type {GetType().Name} was mutated after being locked. " +
                        "Did you forget to call Clone() before modifying the state?");
                }
            }
            catch (JsonException)
            {
                // If serialization fails now but succeeded before, something changed
                throw new StateLockedException(
                    $"JsonState of type {GetType().Name} appears to have been mutated after being locked. " +
                    "Did you forget to call Clone() before modifying the state?");
            }
        }
    }

    /// <summary>
    /// Custom type info resolver that handles [JsonAtomic] attributes.
    /// - Excludes properties marked with [JsonAtomic] from serialization
    /// - Optionally includes the fingerprint properties specified in [JsonAtomic]
    /// </summary>
    internal class AtomicExcludingTypeInfoResolver : DefaultJsonTypeInfoResolver
    {
        private readonly bool _includeFingerprintProperties;

        public AtomicExcludingTypeInfoResolver(bool includeFingerprintProperties)
        {
            _includeFingerprintProperties = includeFingerprintProperties;
        }

        public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = base.GetTypeInfo(type, options);

            if (typeInfo.Kind != JsonTypeInfoKind.Object)
                return typeInfo;

            // Track fingerprint property names:
            // - To wrap with exception handling (always, for clearer error messages)
            // - To add if not already present (when including fingerprints)
            // - To exclude (when not including fingerprints)
            var fingerprintPropsToWrap = new HashSet<string>();
            var fingerprintPropsToAdd = new List<(string Name, PropertyInfo PropInfo)>();

            // Process existing properties
            foreach (var jsonProp in typeInfo.Properties)
            {
                var propInfo = type.GetProperty(jsonProp.Name, BindingFlags.Public | BindingFlags.Instance);
                if (propInfo == null)
                    continue;

                var atomicAttr = propInfo.GetCustomAttribute<JsonAtomicAttribute>();
                if (atomicAttr != null)
                {
                    // Exclude atomic property from serialization
                    jsonProp.ShouldSerialize = (_, _) => false;

                    if (!string.IsNullOrEmpty(atomicAttr.FingerprintProperty))
                    {
                        // Always track for wrapping with exception handling
                        fingerprintPropsToWrap.Add(atomicAttr.FingerprintProperty);

                        if (_includeFingerprintProperties)
                        {
                            // Remember to add fingerprint property if not already present
                            var fpPropInfo = type.GetProperty(atomicAttr.FingerprintProperty, BindingFlags.Public | BindingFlags.Instance);
                            if (fpPropInfo != null)
                            {
                                fingerprintPropsToAdd.Add((atomicAttr.FingerprintProperty, fpPropInfo));
                            }
                        }
                    }
                }
            }

            // Wrap fingerprint property getters to provide clearer error messages if they throw.
            // Fingerprint properties are expected not to throw - if they do, we wrap the exception.
            // Also exclude from serialization if not including fingerprints.
            foreach (var jsonProp in typeInfo.Properties)
            {
                if (fingerprintPropsToWrap.Contains(jsonProp.Name))
                {
                    if (!_includeFingerprintProperties)
                    {
                        // Exclude from serialization when not including fingerprints
                        jsonProp.ShouldSerialize = (_, _) => false;
                    }

                    // Wrap getter for clearer error messages
                    var originalGet = jsonProp.Get;
                    var propName = jsonProp.Name;
                    jsonProp.Get = (obj) =>
                    {
                        try
                        {
                            return originalGet?.Invoke(obj);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"Fingerprint property '{propName}' on type '{obj?.GetType().Name}' threw an exception. " +
                                "Fingerprint properties should not throw exceptions.", ex);
                        }
                    };
                }
            }

            // Add fingerprint properties that are not already present (getter-only properties)
            // Wrap getters to provide clear error messages if they throw.
            foreach (var (name, propInfo) in fingerprintPropsToAdd)
            {
                // Check if property already exists in serialization
                bool alreadyExists = false;
                foreach (var existingProp in typeInfo.Properties)
                {
                    if (existingProp.Name == name)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    var jsonProp = typeInfo.CreateJsonPropertyInfo(propInfo.PropertyType, name);
                    var propName = name;
                    jsonProp.Get = (obj) =>
                    {
                        try
                        {
                            return propInfo.GetValue(obj);
                        }
                        catch (Exception ex)
                        {
                            // Unwrap TargetInvocationException if present
                            var innerEx = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                            throw new InvalidOperationException(
                                $"Fingerprint property '{propName}' on type '{obj?.GetType().Name}' threw an exception. " +
                                "Fingerprint properties should not throw exceptions.", innerEx);
                        }
                    };
                    typeInfo.Properties.Add(jsonProp);
                }
            }

            return typeInfo;
        }
    }

    /// <summary>
    /// A JsonConverterFactory that ensures dictionaries are serialized with
    /// sorted keys for deterministic output.
    /// </summary>
    internal class SortedDictionaryConverterFactory : JsonConverterFactory
    {
        /// <summary>
        /// The set of dictionary key types that are supported by JsonState.
        /// These types have deterministic string representations and can be sorted.
        /// </summary>
        private static readonly HashSet<Type> SupportedKeyTypes = new HashSet<Type>
        {
            typeof(string),
            typeof(int),
            typeof(long),
            typeof(Guid)
        };

        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsGenericType)
            {
                return false;
            }

            var genericDef = typeToConvert.GetGenericTypeDefinition();
            return genericDef == typeof(Dictionary<,>) ||
                   genericDef == typeof(IDictionary<,>);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var keyType = typeToConvert.GetGenericArguments()[0];
            var valueType = typeToConvert.GetGenericArguments()[1];

            // Validate that the key type is supported
            if (!SupportedKeyTypes.Contains(keyType))
            {
                throw new UnsupportedDictionaryKeyTypeException(keyType);
            }

            var converterType = typeof(SortedDictionaryConverter<,>).MakeGenericType(keyType, valueType);
            return (JsonConverter)Activator.CreateInstance(converterType);
        }
    }

    /// <summary>
    /// A JsonConverter that serializes dictionaries with sorted keys.
    /// Manually handles reading/writing to preserve reference context across the object graph.
    /// </summary>
    internal class SortedDictionaryConverter<TKey, TValue> : JsonConverter<Dictionary<TKey, TValue>>
    {
        public override Dictionary<TKey, TValue> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject for dictionary");
            }

            var dict = new Dictionary<TKey, TValue>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return dict;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName");
                }

                // Read the key
                string keyString = reader.GetString();
                TKey key = ConvertKey(keyString);

                // Move to the value
                reader.Read();

                // Deserialize the value using SAME options - preserves reference context
                TValue value = JsonSerializer.Deserialize<TValue>(ref reader, options);

                dict[key] = value;
            }

            throw new JsonException("Unexpected end of JSON while reading dictionary");
        }

        public override void Write(
            Utf8JsonWriter writer,
            Dictionary<TKey, TValue> value,
            JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            // Sort keys for deterministic output
            var sortedKeys = new List<TKey>(value.Keys);
            sortedKeys.Sort(Comparer<TKey>.Default);

            writer.WriteStartObject();
            foreach (var key in sortedKeys)
            {
                var keyString = key?.ToString() ?? "null";
                writer.WritePropertyName(keyString);

                // Use SAME options - preserves reference context
                JsonSerializer.Serialize(writer, value[key], options);
            }
            writer.WriteEndObject();
        }

        private static TKey ConvertKey(string keyString)
        {
            if (typeof(TKey) == typeof(string))
            {
                return (TKey)(object)keyString;
            }

            if (typeof(TKey) == typeof(int))
            {
                return (TKey)(object)int.Parse(keyString);
            }

            if (typeof(TKey) == typeof(long))
            {
                return (TKey)(object)long.Parse(keyString);
            }

            if (typeof(TKey) == typeof(Guid))
            {
                return (TKey)(object)Guid.Parse(keyString);
            }

            // Fallback for other types
            return (TKey)Convert.ChangeType(keyString, typeof(TKey));
        }
    }

    /// <summary>
    /// A custom ReferenceHandler that maintains reference state across multiple
    /// serialize/deserialize calls. This is necessary because the default
    /// ReferenceHandler.Preserve creates a fresh resolver per call, which breaks
    /// reference tracking when custom converters call JsonSerializer recursively.
    /// </summary>
    internal class PreserveReferenceHandler : ReferenceHandler
    {
        private readonly PreserveReferenceResolver _resolver;

        public PreserveReferenceHandler()
        {
            _resolver = new PreserveReferenceResolver();
        }

        public override ReferenceResolver CreateResolver() => _resolver;
    }

    /// <summary>
    /// A ReferenceResolver that tracks object references using $id and $ref
    /// throughout the entire serialization/deserialization process.
    /// </summary>
    internal class PreserveReferenceResolver : ReferenceResolver
    {
        private uint _referenceCount;
        private readonly Dictionary<string, object> _referenceIdToObjectMap = new Dictionary<string, object>();
        private readonly Dictionary<object, string> _objectToReferenceIdMap = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);

        public override void AddReference(string referenceId, object value)
        {
            if (!_referenceIdToObjectMap.ContainsKey(referenceId))
            {
                _referenceIdToObjectMap[referenceId] = value;
            }
        }

        public override string GetReference(object value, out bool alreadyExists)
        {
            if (_objectToReferenceIdMap.TryGetValue(value, out string referenceId))
            {
                alreadyExists = true;
                return referenceId;
            }

            _referenceCount++;
            referenceId = _referenceCount.ToString();
            _objectToReferenceIdMap[value] = referenceId;
            alreadyExists = false;
            return referenceId;
        }

        public override object ResolveReference(string referenceId)
        {
            if (_referenceIdToObjectMap.TryGetValue(referenceId, out object value))
            {
                return value;
            }

            throw new JsonException($"Reference '{referenceId}' not found.");
        }
    }

    /// <summary>
    /// Compares objects by reference equality rather than value equality.
    /// </summary>
    internal class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

        private ReferenceEqualityComparer() { }

        public new bool Equals(object x, object y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
