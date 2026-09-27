using System.Globalization;
using System.Text;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Language.Corpus;

/// <summary>A model as text, blind to spans: the frozen corpus's goldens (<c>D-178</c>).</summary>
/// <remarks>
/// It leaves out what the model's meaning does not depend on: every span; a curve's driver, whose effect is in every
/// parameter that reads the curve; the lets and design values themselves, whose effect is in every parameter that
/// reads them; a component's origin. A circuit's mode and a run's events are included only when the file has a run,
/// against the run's projection. The goldens were taken through it, so its rendering is frozen with them.
/// </remarks>
public static class ModelShape
{
    /// <summary>Renders a model.</summary>
    /// <param name="model">The model.</param>
    /// <param name="withRun">Whether to include the circuits' modes and the schedule.</param>
    /// <returns>One line per fact, in a stable order.</returns>
    public static string Of(SemanticModel model, bool withRun)
    {
        ArgumentNullException.ThrowIfNull(model);
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture, $"project {model.Project.Name ?? "project"} cases [{string.Join(",", model.Project.Scenarios)}] design {model.Project.DesignScenario ?? model.Project.Scenarios.FirstOrDefault()} start {model.Project.Start}");
        text.AppendLine(CultureInfo.InvariantCulture, $"style spacing {model.Style.Spacing} default {model.Style.Default}");

        foreach (var circuit in model.Circuits)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"circuit {circuit.Name} {circuit.Number} {circuit.Substance} {circuit.Role.CanonicalName}{(withRun ? " " + circuit.Mode : string.Empty)}");
        }

        foreach (var component in model.Components.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"component {component.Name} {component.Kind?.Keyword} in {component.CircuitName} at {component.AttachedTo} tag {component.Tag} style {component.Style}");
            foreach (var (key, point) in component.SizingPoint.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  sized at {key} = {point.Number:G6}");
            }

            foreach (var (key, capacity) in component.Capacities.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  capacity {key} = {capacity.SiValue:G6}");
            }

            foreach (var (key, value) in component.Parameters.OrderBy(static p => p.Key, StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {key} = {Value(value)}{(value.Scenarios.IsDefaultOrEmpty ? string.Empty : " [" + string.Join(" | ", value.Scenarios.Select(Value)) + "]")}");
            }
        }

        foreach (var line in model.Connections.Select(static c => $"connection {c.From.Component}.{c.From.Port} -> {c.To.Component}.{c.To.Port}").Order(StringComparer.Ordinal))
        {
            text.AppendLine(line);
        }

        foreach (var control in model.ControlBindings.OrderBy(static c => c.Controller.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"control {control.Controller.Name} moves {control.Actuator} reads {control.Measurement} setpoint {Quantity(control.Setpoint)} [{string.Join(" | ", control.Setpoints.Select(Quantity))}] band {Quantity(control.Band)} differential {Quantity(control.Differential)} output {Quantity(control.OutputLow)}..{Quantity(control.OutputHigh)} curve {control.Curve}");
        }

        foreach (var curve in model.Curves.OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"curve {curve.Name} extrapolated {curve.IsExtrapolated} [{string.Join(" ", curve.Points.Select(static p => $"{p.X.ToString("R", CultureInfo.InvariantCulture)}:{p.Y.ToString("R", CultureInfo.InvariantCulture)}"))}]");
        }

        if (withRun)
        {
            foreach (var disturbance in model.Disturbances)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"event {disturbance.Circuit} {disturbance.Target} {Quantity(disturbance.From)}..{Quantity(disturbance.To)} {Quantity(disturbance.FromValue)} -> {Quantity(disturbance.ToValue)}");
            }
        }

        return text.ToString();
    }

    private static string Value(ParameterValue value) =>
        $"{Quantity(value.Value)} {value.Symbol} {value.Reference}";

    private static string Quantity(Quantity? quantity) =>
        quantity is { } q ? $"{q.SiValue.ToString("R", CultureInfo.InvariantCulture)} {q.Dimension}" : "-";
}
