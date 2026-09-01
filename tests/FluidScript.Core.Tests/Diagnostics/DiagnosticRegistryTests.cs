using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Tests.Diagnostics;

/// <summary>
/// The registry's own invariants, several of which pass over an empty collection today.
/// </summary>
/// <remarks>
/// That is the intended state. The registry grows one work package at a time, and a rule asserted
/// before there is anything to break is a guard rail; the same rule added after fifty codes exist is
/// a clean-up nobody schedules. <see cref="MessageStyleRulesTests"/> is what proves the checks these
/// tests apply are not themselves inert.
/// </remarks>
public sealed class DiagnosticRegistryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void All_IsOrderedByCode()
    {
        var codes = DiagnosticRegistry.All.Select(static descriptor => descriptor.Code).ToArray();

        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void All_DefinesEachCodeOnce()
    {
        // Invariant 2 follows from this one: a code defined once cannot be emitted with two
        // severities, because the descriptor is the only place a severity is stated.
        var duplicates = DiagnosticRegistry.All
            .GroupBy(static descriptor => descriptor.Code, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        Assert.True(duplicates.Length == 0, $"Defined more than once: {string.Join(", ", duplicates)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Retired_CodesAreNeverAllocatedAgain()
    {
        // Invariant 7. A code whose old condition now parses cleanly is the dangerous case: every
        // existing reference to it silently becomes wrong, and nothing announces it.
        var reused = DiagnosticRegistry.Retired
            .Where(static retired => DiagnosticRegistry.TryGet(retired.Code, out _))
            .Select(static retired => retired.Code)
            .ToArray();

        Assert.True(reused.Length == 0, $"Retired and allocated again: {string.Join(", ", reused)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Retired_HoldsTheWithdrawnCircuitHeaderCode()
    {
        Assert.True(DiagnosticRegistry.IsRetired("FS1509"));
        Assert.False(DiagnosticRegistry.TryGet("FS1509", out _));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TryGet_DistinguishesRetiredFromNeverAllocated()
    {
        // The two want opposite responses -- one is a typo, the other is a reference to a rule that
        // changed -- so the registry must not collapse them into a single miss.
        Assert.False(DiagnosticRegistry.TryGet("FS1509", out _));
        Assert.True(DiagnosticRegistry.IsRetired("FS1509"));

        Assert.False(DiagnosticRegistry.TryGet("FS1599", out _));
        Assert.False(DiagnosticRegistry.IsRetired("FS1599"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_RetiredCode_SaysSoRatherThanSayingUnknown()
    {
        var exception = Assert.Throws<ArgumentException>(() => DiagnosticRegistry.Get("FS1509"));

        Assert.Contains("retired", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Get_UnknownCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticRegistry.Get("FS1599"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryMessageMeetsTheStyleRules()
    {
        var violations = DiagnosticRegistry.All
            .SelectMany(
                static descriptor => MessageStyleRules.Violations(descriptor),
                static (descriptor, violation) => $"{descriptor.Code}: {violation}")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
}
