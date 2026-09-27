namespace FluidScript.Core.Model.Contract;

/// <summary>One design parameter, with where it came from (<c>D-02</c>).</summary>
public sealed record ParameterWire
{
    /// <summary>The value in <see cref="Unit"/>, or <see langword="null"/> under <c>FS2501</c> and for a word-valued parameter.</summary>
    public required double? Value { get; init; }

    /// <summary>The canonical unit, or <see langword="null"/> for a dimensionless value.</summary>
    public required string? Unit { get; init; }

    /// <summary><c>stated</c>, <c>sized</c> or <c>default</c>.</summary>
    public required string Source { get; init; }

    /// <summary>Why a sized or default value is what it is; absent for a stated one.</summary>
    [AbsentWhenNull]
    public string? Basis { get; init; }

    /// <summary>A word-valued parameter's word, such as a pipe's <c>material</c>; absent for a number (<c>C-147</c>, contract 4.1).</summary>
    /// <remarks><see cref="Value"/> and <see cref="Unit"/> are <see langword="null"/> beside it: a word has neither.</remarks>
    [AbsentWhenNull]
    public string? Text { get; init; }
}
