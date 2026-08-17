// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Specmine.Tests;

using NUnit.Framework;

/// <summary>
/// Exercises <see cref="TargetAdapterRegistry"/> in isolation: registration and
/// resolution, duplicate registration, unknown adapter types, and the ordinal (exact,
/// case-sensitive) identifier semantics adapter types are compared with.
/// </summary>
[TestFixture]
public sealed class TargetAdapterRegistryTests
{
    [Test]
    public void Resolve_RegisteredAdapter_ReturnsTheSameInstance()
    {
        var registry = new TargetAdapterRegistry();
        var adapter = new StubTargetAdapter("widget");

        registry.Register(adapter);

        Assert.That(registry.Resolve("widget"), Is.SameAs(adapter));
    }

    [Test]
    public void TryResolve_RegisteredAdapter_ReturnsTrueAndTheInstance()
    {
        var registry = new TargetAdapterRegistry();
        var adapter = new StubTargetAdapter("widget");
        registry.Register(adapter);

        var resolved = registry.TryResolve("widget", out var found);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(found, Is.SameAs(adapter));
        });
    }

    [Test]
    public void TryResolve_UnregisteredAdapterType_ReturnsFalseWithoutThrowing()
    {
        var registry = new TargetAdapterRegistry();

        var resolved = registry.TryResolve("does-not-exist", out var found);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(found, Is.Null);
        });
    }

    [Test]
    public void Resolve_UnregisteredAdapterType_ThrowsUnknownAdapterTypeExceptionNamingTheType()
    {
        var registry = new TargetAdapterRegistry();

        var thrown = Assert.Throws<UnknownAdapterTypeException>(() => registry.Resolve("does-not-exist"));

        Assert.That(thrown!.AdapterType, Is.EqualTo("does-not-exist"));
    }

    [Test]
    public void Register_DuplicateAdapterTypeFromADifferentInstance_Throws()
    {
        var registry = new TargetAdapterRegistry();
        registry.Register(new StubTargetAdapter("widget"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTargetAdapter("widget")));
    }

    [Test]
    public void Register_SameInstanceTwice_Throws()
    {
        var registry = new TargetAdapterRegistry();
        var adapter = new StubTargetAdapter("widget");
        registry.Register(adapter);

        Assert.Throws<InvalidOperationException>(() => registry.Register(adapter));
    }

    [Test]
    public void Register_DuplicateRegistration_LeavesTheOriginalAdapterResolvable()
    {
        var registry = new TargetAdapterRegistry();
        var original = new StubTargetAdapter("widget");
        registry.Register(original);

        Assert.Throws<InvalidOperationException>(() => registry.Register(new StubTargetAdapter("widget")));

        // A failed registration attempt must not have displaced the adapter already
        // holding the type.
        Assert.That(registry.Resolve("widget"), Is.SameAs(original));
    }

    [Test]
    public void Register_DifferentCasingsOfTheSameName_AreDistinctAdapterTypes()
    {
        var registry = new TargetAdapterRegistry();
        var lower = new StubTargetAdapter("widget");
        var upper = new StubTargetAdapter("WIDGET");

        registry.Register(lower);
        registry.Register(upper);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Resolve("widget"), Is.SameAs(lower));
            Assert.That(registry.Resolve("WIDGET"), Is.SameAs(upper));
        });
    }

    [Test]
    public void Resolve_UsesOrdinalComparison_DifferingOnlyByCasingIsUnknown()
    {
        var registry = new TargetAdapterRegistry();
        registry.Register(new StubTargetAdapter("widget"));

        Assert.Throws<UnknownAdapterTypeException>(() => registry.Resolve("Widget"));
    }

    [Test]
    public void Register_NullAdapter_Throws()
    {
        var registry = new TargetAdapterRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Test]
    public void Resolve_BlankAdapterType_Throws()
    {
        var registry = new TargetAdapterRegistry();

        Assert.Throws<ArgumentException>(() => registry.Resolve("   "));
    }

    [Test]
    public void TryResolve_BlankAdapterType_Throws()
    {
        var registry = new TargetAdapterRegistry();

        Assert.Throws<ArgumentException>(() => registry.TryResolve("   ", out _));
    }
}
