namespace KeyValueStore.Tests;

using Microsoft.Accordant;

/// <summary>
/// Spec: defines what each operation should do.
/// Each operation handles error cases first, then the success case.
/// </summary>
public static class KvSpec
{
    public static Spec<KvState> Create()
    {
        var spec = new Spec<KvState>()
            .WithJsonPrinters();

        // PUT: Create or update a key
        spec.Operation<PutRequest, ApiResult>("Put", (request, state) =>
        {
            // Put always succeeds (upsert semantics)
            return Expect.That<ApiResult>(r => r.StatusCode == 200,
                       $"Should return 200 OK")
                   .ThenState<KvState>(nextState =>
                       nextState.Items[request.Key] = request.Value);
        });

        // GET: Retrieve a key
        spec.Operation<string, ApiResult<string>>("Get", (key, state) =>
        {
            // Error case: key doesn't exist
            if (!state.Items.TryGetValue(key, out var value))
            {
                return Expect.That<ApiResult<string>>(r => r.StatusCode == 404,
                           $"Should return 404 because key '{key}' doesn't exist")
                       .SameState();
            }

            // Success case: return the value
            return Expect.That<ApiResult<string>>(
                       r => r.StatusCode == 200 && r.Data == value,
                       $"Should return 200 with value '{value}'")
                   .SameState();
        });

        // DELETE: Remove a key
        spec.Operation<string, ApiResult>("Delete", (key, state) =>
        {
            // Error case: key doesn't exist
            if (!state.Items.ContainsKey(key))
            {
                return Expect.That<ApiResult>(r => r.StatusCode == 404,
                           $"Should return 404 because key '{key}' doesn't exist")
                       .SameState();
            }

            // Success case: remove it
            return Expect.That<ApiResult>(r => r.StatusCode == 204,
                       $"Should return 204 No Content")
                   .ThenState<KvState>(nextState =>
                       nextState.Items.Remove(key));
        });

        return spec;
    }
}

// Simple request/response types
public record PutRequest(string Key, string Value);
public record ApiResult(int StatusCode);
public record ApiResult<T>(int StatusCode, T? Data = default) : ApiResult(StatusCode);
