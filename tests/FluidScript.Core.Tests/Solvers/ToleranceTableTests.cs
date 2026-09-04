using System.Globalization;
using System.Text.RegularExpressions;

using FluidScript.Core.Solvers;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Solvers;

/// <summary>
/// <c>Tolerances</c> is <c>36</c>'s table, and this asserts it against the document itself.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Both directions, because only one of them is the failure that happened.</strong> A value
/// mistyped in the code is caught by checking every constant against its documented row; a value
/// <em>added to the document</em> and never implemented is caught only by checking every documented
/// row against a constant. <c>S-6</c> is the second kind: two numbers were transcribed correctly and
/// then the document moved on without them.
/// </para>
/// <para>
/// The document is the source and the code is the copy, which is the direction that makes the plan
/// worth keeping. A change to the table fails this test until the constant follows it.
/// </para>
/// </remarks>
public sealed partial class ToleranceTableTests
{
    /// <summary>Every key <c>36</c> tabulates, against the constant that implements it.</summary>
    private static readonly (string Key, double Value)[] Shipped =
    [
        ("newton.residual_tol", Tolerances.NewtonResidual),
        ("newton.step_tol", Tolerances.NewtonStep),
        ("newton.max_iterations", Tolerances.NewtonMaxIterations),
        ("newton.divergence_factor", Tolerances.NewtonDivergenceFactor),
        ("newton.line_search_min", Tolerances.NewtonLineSearchMin),
        ("newton.fd_step", Tolerances.NewtonFiniteDifferenceStep),
        ("jacobian.singular_tol", Tolerances.JacobianSingular),
        ("outer.max_passes", Tolerances.OuterMaxPasses),
        ("outer.relative_tol", Tolerances.OuterRelative),
        ("transient.cfl_safety", Tolerances.TransientCflSafety),
        ("transient.local_error_tol", Tolerances.TransientLocalError),
        ("transient.max_step", Tolerances.TransientMaxStep),
        ("transient.min_step", Tolerances.TransientMinStep),
        ("transient.energy_drift_tol", Tolerances.TransientEnergyDriftWarn),
        ("transient.energy_drift_fail", Tolerances.TransientEnergyDriftFail),
        ("valve.dp_regularization", Tolerances.ValveRegularizationDrop),
        ("upwind.smoothing_band", Tolerances.UpwindSmoothingBand),
        ("flow.zero_tol", Tolerances.FlowZero),
        ("quantity.compare_rel_tol", Tolerances.QuantityCompareRelative),
        ("fixed_point.rel_tol", Tolerances.FixedPointRelative),
        ("optimizer.cache_round", Tolerances.OptimizerCacheRound),
        ("optimizer.population_size", Tolerances.OptimizerPopulationSize),
        ("optimizer.generation_cap", Tolerances.OptimizerGenerationCap),
        ("optimizer.stagnation_generations", Tolerances.OptimizerStagnationGenerations),
        ("optimizer.stagnation_rel_tol", Tolerances.OptimizerStagnationRelative),
        ("scale.pressure", Tolerances.PressureScale),
        ("scale.enthalpy", Tolerances.EnthalpyScale),
        ("scale.flow_floor", Tolerances.FlowScaleFloor),
        ("scale.temperature", Tolerances.TemperatureScale),
    ];

    [GeneratedRegex(@"^\|\s*`([a-z_.]+)`\s*\|([^|]*)\|")]
    private static partial Regex TableRow();

    [GeneratedRegex(@"[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?")]
    private static partial Regex Number();

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryConstantCarriesTheValueTheDocumentGivesIt()
    {
        var documented = Documented();

        foreach (var (key, value) in Shipped)
        {
            Assert.True(documented.ContainsKey(key), $"36's tolerance table has no row for '{key}'.");

            // Relative, because the document writes the finite-difference step as an approximation
            // ("root-epsilon, about 1.49e-8") and every other row exactly.
            Assert.Equal(documented[key], value, 1e-3 * Math.Abs(documented[key]));
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryDocumentedToleranceIsImplemented()
    {
        var shipped = Shipped.Select(static entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        var missing = Documented().Keys.Where(key => !shipped.Contains(key)).Order(StringComparer.Ordinal);

        Assert.True(
            !missing.Any(),
            "36's table has rows nothing implements, so the number lives only in prose: "
            + string.Join(", ", missing));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheScanActuallyFindsTheTable()
    {
        // A pattern that matches nothing makes both assertions above pass while checking nothing.
        Assert.Equal(Shipped.Length, Documented().Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TheFiniteDifferenceStepIsTheSquareRootOfMachineEpsilon()
    {
        // The document writes it as an approximation; the code cannot, because the step size is what
        // balances truncation against round-off and a rounded copy of it is a slightly worse Jacobian
        // for no reason. Math.Sqrt is not a compile-time constant, so the literal is checked instead.
        Assert.Equal(Tolerances.NewtonFiniteDifferenceStep, Math.Sqrt(Math.Pow(2, -52)));
    }

    /// <summary>Reads <c>36</c>'s tolerance table.</summary>
    /// <returns>Each key against the value the document gives it.</returns>
    /// <remarks>
    /// Scoped to the one section, because the failure-taxonomy table below it also opens its rows with
    /// a backticked identifier and would otherwise be read as tolerances with no values.
    /// </remarks>
    private static Dictionary<string, double> Documented()
    {
        var text = File.ReadAllText(
            Path.Combine(RepositoryLayout.Root, "plan", "30-solver", "36-numerics-and-convergence.md"));

        var start = text.IndexOf("## The tolerance table", StringComparison.Ordinal);
        Assert.True(start >= 0, "36 no longer has a section called 'The tolerance table'.");

        var end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        var values = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var line in section.Split('\n'))
        {
            var row = TableRow().Match(line.Trim());

            if (!row.Success)
            {
                continue;
            }

            values[row.Groups[1].Value] = Parse(row.Groups[2].Value.Trim());
        }

        return values;
    }

    /// <summary>Reads one value cell.</summary>
    /// <param name="cell">The cell, which may carry a unit, an emphasis marker or a fraction.</param>
    /// <returns>The number.</returns>
    private static double Parse(string cell)
    {
        var text = cell.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (text.Contains("√ε", StringComparison.Ordinal))
        {
            return Math.Sqrt(Math.Pow(2, -52));
        }

        var numbers = Number().Matches(text);
        Assert.True(numbers.Count > 0, $"'{cell}' carries no number.");

        var first = double.Parse(numbers[0].Value, CultureInfo.InvariantCulture);

        // A fraction, as newton.line_search_min is written: 1/64 rather than 0.015625.
        return numbers.Count > 1 && text.Contains('/', StringComparison.Ordinal)
            ? first / double.Parse(numbers[1].Value, CultureInfo.InvariantCulture)
            : first;
    }
}
