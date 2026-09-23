namespace FluidScript.Core.Language.Binding;

/// <summary>A stable identity for any value that may participate in evaluation.</summary>
public abstract record ValueId
{
    private ValueId()
    {
    }

    /// <summary>A <c>let</c> binding.</summary>
    /// <param name="Name">The bound name.</param>
    public sealed record Let(string Name) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>A parameter written on a component.</summary>
    /// <param name="Component">The component's name.</param>
    /// <param name="Parameter">The canonical parameter name.</param>
    public sealed record ComponentParameter(string Component, string Parameter) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component}.{Parameter}";
    }

    /// <summary>One element of a parameter's per-scenario value list (<c>D-143</c>).</summary>
    /// <param name="Component">The component's name.</param>
    /// <param name="Parameter">The canonical parameter name.</param>
    /// <param name="Scenario">The element's position, which is the scenario's position.</param>
    /// <remarks>
    /// Separate from <see cref="ComponentParameter"/> because each element is evaluated on its own:
    /// an element may read a curve or a <c>let</c> the others do not, so they are different nodes in
    /// the dependency graph even though they are one line in the file.
    /// </remarks>
    public sealed record ScenarioParameter(string Component, string Parameter, int Scenario) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component}.{Parameter}[{Scenario}]";
    }

    /// <summary>A property read off a component.</summary>
    /// <param name="Component">The component's name.</param>
    /// <param name="Property">The canonical property name.</param>
    public sealed record ComponentProperty(string Component, string Property) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component}.{Property}";
    }

    /// <summary>A curve, read at its driver's design value (<c>D-57</c>).</summary>
    /// <param name="Name">The curve's name.</param>
    /// <remarks>
    /// A node like any other, which is what makes a cycle among curves the same <c>FS1402</c> a cycle
    /// among <c>let</c> bindings already was, reported by the same depth-first sort.
    /// </remarks>
    public sealed record Curve(string Name) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>One driver's value at the design condition (<c>D-58</c>).</summary>
    /// <param name="Driver">The canonical driver name the <c>design</c> line was keyed under.</param>
    public sealed record Design(string Driver) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"design {Driver}";
    }

    /// <summary>One driver's value at a component's own sizing point (<c>D-94</c>).</summary>
    /// <param name="Component">The component that wrote the <c>sized_at</c> clause.</param>
    /// <param name="Driver">The canonical driver name the clause's argument was keyed under.</param>
    /// <remarks>
    /// A node of its own so the component's parameters are ordered after it: a curve they read is
    /// evaluated at this value rather than at the file's <see cref="Design"/>.
    /// </remarks>
    public sealed record SizingPoint(string Component, string Driver) : ValueId
    {
        /// <inheritdoc/>
        public override string ToString() => $"{Component} sized_at {Driver}";
    }
}
