using System.Collections.Immutable;

using FluidScript.Core.Components;
using FluidScript.Core.Diagnostics.Descriptors;
using FluidScript.Core.Physics.Units;
using FluidScript.Core.Primitives;

namespace FluidScript.Core.Sizing;

/// <summary>A sizing rule for one family of component, handed only that family.</summary>
/// <typeparam name="TComponent">The components the rule sizes.</typeparam>
/// <remarks>
/// <para>
/// <strong>The type test is written once, and the rule receives the type it tested for</strong>
/// (<c>D-147</c>). Every sizer used to open with the same three steps: a <c>CanSize</c> that tested the
/// type, a <c>Size</c> that tested it again and cast, and a refusal for the case the loop never sends.
/// A rule written against <typeparamref name="TComponent"/> cannot be handed the wrong component, and
/// cannot forget to refuse one.
/// </para>
/// <para>
/// A rule narrower than its type — the thermal rule sizes only an exchanger with a rating — narrows
/// <see cref="CanSize"/> and refuses the rest itself, through <see cref="Refused"/>.
/// </para>
/// </remarks>
public abstract class SizerBase<TComponent> : ISizer
    where TComponent : class, IFlowComponent
{
    /// <inheritdoc/>
    public abstract ImmutableArray<string> Parameters { get; }

    /// <inheritdoc/>
    public virtual ImmutableDictionary<string, Quantity> Provisional => [];

    /// <summary>Gets what the refusal says the rule could not choose, and what it was handed instead.</summary>
    /// <value>
    /// <c>Property</c> names the quantity (<c>"a diameter"</c>); <c>State</c> describes the component the
    /// rule does not size (<c>"a component that is not a pipe"</c>).
    /// </value>
    protected abstract (string Property, string State) Refusal { get; }

    /// <inheritdoc/>
    public virtual bool CanSize(IFlowComponent component) => component is TComponent;

    /// <inheritdoc/>
    public Result<SizingResult> Size(IFlowComponent component, in SizingContext context)
    {
        ArgumentNullException.ThrowIfNull(component);

        return component is TComponent typed ? Size(typed, context) : Refused(component);
    }

    /// <summary>Chooses values for whatever <see cref="Parameters"/> this component left open.</summary>
    /// <param name="component">The component to size, already of the rule's family.</param>
    /// <param name="context">The flow and state to size against.</param>
    /// <returns>The values with their bases, or why the estimate was not enough to decide.</returns>
    protected abstract Result<SizingResult> Size(TComponent component, in SizingContext context);

    /// <summary>The refusal for a component this rule does not size.</summary>
    /// <param name="component">The component the rule was handed.</param>
    /// <returns>A failure naming the component and <see cref="Refusal"/>.</returns>
    protected Result<SizingResult> Refused(IFlowComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        return Result.Failure<SizingResult>(ResultError.From(
            FluidDiagnostics.PropertyNotEvaluable,
            ("property", Refusal.Property),
            ("name", component.Name),
            ("state", Refusal.State)));
    }
}
