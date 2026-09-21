using System.Globalization;
using System.Text;
using FluidScript.Core.Fluids;
using FluidScript.Core.Units;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Performance;

/// <summary>How a substance's (p,h) flash responds to the perturbations a Jacobian column uses, against the step that clears its noise (<c>S-74</c>).</summary>
/// <remarks>
/// A forward difference assumes the function is exact to round-off; an iterated flash is not. This
/// writes <c>diagnostics/flash-noise.md</c>: the temperature and density derivatives in pressure and
/// in enthalpy at a range of step sizes, so the step the solver uses can be read against where the
/// derivative settles. It is what settled <c>newton.fd_step_state</c>.
/// </remarks>
[Trait("Category", "Diagnostic")]
public sealed class FlashNoiseDiagnostics
{
    [Fact]
    public async Task SurveyWater()
    {
        var report = new StringBuilder()
            .AppendLine("# Flash noise")
            .AppendLine()
            .AppendLine("Written by `FlashNoiseDiagnostics`. Forward differences of water's (p,h) flash at a range of")
            .AppendLine("step sizes. The Jacobian's √ε step on a pressure scale of 1e5 Pa is 1.5 mPa; on an enthalpy")
            .AppendLine("scale of 1e5 J/kg it is 1.5 mJ/kg. Where a column's reading does not agree with the settled")
            .AppendLine("value, the step is inside the flash's noise (`S-74`, `36`).")
            .AppendLine();

        var water = Water.Instance;

        foreach (var (p0, h0) in new[] { (250e3, 84e3), (280e3, 209_660.0), (230e3, 251e3), (600e3, 300e3) })
        {
            var basis = State(water, p0, h0);

            report.AppendLine(CultureInfo.InvariantCulture,
                $"## p {p0 / 1000:0} kPa, h {h0 / 1000:0.#} kJ/kg: T {basis.Temperature.SiValue - 273.15:0.00} °C, ρ {basis.Density.SiValue:0.00} kg/m³")
                .AppendLine()
                .AppendLine("| step | dT/dp K/Pa | dρ/dp kg/m³/Pa | dT/dh K/(J/kg) | dρ/dh kg/m³/(J/kg) |")
                .AppendLine("|---|---|---|---|---|");

            foreach (var delta in new[] { 1e-4, 1.5e-3, 1e-2, 1e-1, 1, 2.5, 10, 100, 1000 })
            {
                var byP = State(water, p0 + delta, h0);
                var byH = State(water, p0, h0 + delta);

                report.AppendLine(CultureInfo.InvariantCulture,
                    $"| {delta:0.####E+0} | {(byP.Temperature.SiValue - basis.Temperature.SiValue) / delta:0.000E+0} "
                    + $"| {(byP.Density.SiValue - basis.Density.SiValue) / delta:0.000E+0} "
                    + $"| {(byH.Temperature.SiValue - basis.Temperature.SiValue) / delta:0.000E+0} "
                    + $"| {(byH.Density.SiValue - basis.Density.SiValue) / delta:0.000E+0} |");
            }

            report.AppendLine();
        }

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryLayout.Diagnostics, "flash-noise.md"), report.ToString(), TestContext.Current.CancellationToken);
    }

    private static FluidState State(Water substance, double pressure, double enthalpy)
    {
        var state = substance.FromPressureEnthalpy(
            Quantity.FromSi(pressure, Dimension.Pressure), Quantity.FromSi(enthalpy, Dimension.Enthalpy));

        Assert.True(state.IsSuccess, state.Error?.Message);

        return state.Value;
    }
}
