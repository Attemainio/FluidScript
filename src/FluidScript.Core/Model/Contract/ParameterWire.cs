namespace FluidScript.Core.Model.Contract;

/// <summary>One design parameter, with where it came from (<c>D-02</c>).</summary>
public sealed record ParameterWire
{
    /// <summary>The value in <see cref="Unit"/>, or <see langword="null"/> under <c>FS2501</c>.</summary>
    public required double? Value { get; init; }

    /// <summary>The canonical unit, or <see langword="null"/> for a dimensionless value.</summary>
    public required string? Unit { get; init; }

    /// <summary><c>stated</c>, <c>sized</c> or <c>default</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Why a sized or default value is what it is; absent for a stated one.</summary>
    [AbsentWhenNull]
    public string? Basis { get; init; }
}
