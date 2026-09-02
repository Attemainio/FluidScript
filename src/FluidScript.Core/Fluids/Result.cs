using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using FluidScript.Core.Diagnostics;

namespace FluidScript.Core.Fluids;

/// <summary>Why a property request could not be answered.</summary>
/// <param name="Descriptor">The code this would be reported under, if anything reports it.</param>
/// <param name="Arguments">The values its message needs.</param>
/// <remarks>
/// <para>
/// <strong>An error is carried, not emitted</strong>, and that separation is <c>21</c>'s invariant 4.
/// A solver overshoots during iteration and asks for a state that does not exist, then backtracks;
/// emitting a diagnostic there would put hundreds of errors in the log for a circuit that solved
/// correctly. So this holds everything a diagnostic needs and leaves the decision to a caller that
/// knows whether the state was a trial point or the converged answer.
/// </para>
/// <para>
/// It carries no span because nothing at this layer has one. <see cref="At"/> is where a caller that
/// does supplies it.
/// </para>
/// </remarks>
public sealed record ResultError(DiagnosticDescriptor Descriptor, ImmutableArray<DiagnosticArgument> Arguments)
{
    /// <summary>Gets the code, such as <c>FS2003</c>.</summary>
    public string Code => Descriptor.Code;

    /// <summary>Gets the rendered message.</summary>
    public string Message => Descriptor.Render(Arguments.AsSpan());

    /// <summary>Builds the diagnostic this error would be reported as.</summary>
    /// <param name="span">Where in the source to anchor it, or <see langword="null"/> for a whole-model report.</param>
    /// <returns>The diagnostic.</returns>
    public Diagnostic At(TextSpan? span) => Diagnostic.Create(Descriptor, span, Arguments.AsSpan());

    /// <summary>Builds an error from a descriptor and its arguments.</summary>
    /// <param name="descriptor">The code.</param>
    /// <param name="arguments">Its message's values, as name and text pairs.</param>
    /// <returns>The error.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static ResultError From(DiagnosticDescriptor descriptor, params (string Name, string Value)[] arguments)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(arguments);

        var built = ImmutableArray.CreateBuilder<DiagnosticArgument>(arguments.Length);

        foreach (var (name, value) in arguments)
        {
            built.Add(new DiagnosticArgument(name, value));
        }

        return new ResultError(descriptor, built.ToImmutable());
    }
}

/// <summary>A value, or the reason there is none.</summary>
/// <typeparam name="T">What a success carries.</typeparam>
/// <remarks>
/// <para>
/// <c>21</c> requires every <see cref="ISubstance"/> method to return one of these and none to throw
/// for an out-of-range request. A state request outside the valid range is <em>ordinary</em>: it
/// happens on nearly every Newton step of a circuit that converges perfectly well, and exceptions on
/// that path would be both slow and wrong.
/// </para>
/// <para>
/// It lives beside the fluids because they are its only caller today. A second consumer is the moment
/// to move it, not before — a shared type with one user is a namespace decision made from a guess.
/// </para>
/// </remarks>
public readonly record struct Result<T>
{
    private readonly T? _value;

    internal Result(bool succeeded, T? value, ResultError? error)
    {
        IsSuccess = succeeded;
        _value = value;
        Error = error;
    }

    /// <summary>Gets whether the request was answered.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>Gets why there is no value, or <see langword="null"/> on success.</summary>
    public ResultError? Error { get; }

    /// <summary>Gets the value.</summary>
    /// <exception cref="InvalidOperationException">
    /// The result is a failure. Reading a value that was never produced is a defect in the caller, not
    /// a condition to handle — check <see cref="IsSuccess"/> or use <see cref="TryGetValue"/>.
    /// </exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"No value: {Error?.Code} {Error?.Message}");

    /// <summary>Reads the value when there is one.</summary>
    /// <param name="value">The value, or its default.</param>
    /// <returns><see langword="true"/> when the result succeeded.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return IsSuccess;
    }
}

/// <summary>Builds a <see cref="Result{T}"/>.</summary>
/// <remarks>
/// The factories live here rather than on the generic type because a public static member on a
/// generic one has to be reached as <c>Result&lt;FluidState&gt;.Success(...)</c>, repeating a type
/// argument the value already carries. <c>Result.Success(state)</c> infers it.
/// </remarks>
public static class Result
{
    /// <summary>Wraps a value that was produced.</summary>
    /// <typeparam name="T">What the result carries.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success<T>(T value) => new(true, value, null);

    /// <summary>Wraps the reason no value was produced.</summary>
    /// <typeparam name="T">What the result would have carried.</typeparam>
    /// <param name="error">Why there is none.</param>
    /// <returns>A failed result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<T> Failure<T>(ResultError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new Result<T>(false, default, error);
    }
}
