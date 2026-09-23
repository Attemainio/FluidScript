namespace FluidScript.Core.Language.Registry;

/// <summary>How a written name ranked against a closed set.</summary>
/// <typeparam name="T">What a candidate resolves to.</typeparam>
/// <param name="Best">The best candidate, or <see langword="null"/> when the set is empty.</param>
/// <param name="BestScore">Its score; 1.0 for an exact match.</param>
/// <param name="RunnerUp">The next best candidate, or <see langword="null"/>.</param>
/// <param name="RunnerUpScore">The runner-up's score.</param>
/// <param name="IsExact">Whether <paramref name="Best"/> was reached by exact match rather than scoring.</param>
public readonly record struct NameMatch<T>(
    T? Best,
    double BestScore,
    T? RunnerUp,
    double RunnerUpScore,
    bool IsExact)
    where T : class
{
    /// <summary>Gets whether the winner is clear of its runner-up by the ambiguity margin.</summary>
    public bool IsClear =>
        RunnerUp is null || BestScore - RunnerUpScore > NameResolution.AmbiguityMargin;
}
