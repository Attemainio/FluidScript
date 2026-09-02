using System.Collections.Immutable;

namespace FluidScript.Core.Language;

/// <summary>
/// How a written name is matched against a closed set the registry owns — a kind, a parameter, a
/// property, or a symbol parameter's accepted values (<c>D-15</c>).
/// </summary>
/// <remarks>
/// <para>
/// Three stages, and the first two are exact. Normalise, look up, and only then score. A user should
/// not have to learn a canonical spelling to declare a valve, and an agent writing a script from a
/// brief produces <c>heat-exchanger</c>, <c>HeatExchanger</c>, <c>exchanger</c> and
/// <c>heatexchanger</c> with roughly equal probability.
/// </para>
/// <para>
/// This never runs on component <em>names</em>. <c>PU1</c> and <c>PUI</c> are different components,
/// and a typo in a name must produce a dangling reference rather than a silent merge.
/// </para>
/// </remarks>
public static class NameResolution
{
    /// <summary>The score a candidate must reach to resolve at all.</summary>
    /// <value>
    /// 0.70. <c>pmp</c> against <c>pump</c> scores 0.75 and must resolve; <c>valve</c> against
    /// <c>pipe</c> scores 0.20 and must not.
    /// </value>
    public const double ResolveThreshold = 0.70;

    /// <summary>How far clear of the runner-up the winner must be.</summary>
    /// <value>
    /// 0.05. Below this nothing resolves and both candidates are reported. Picking the higher of two
    /// near-equal candidates is a coin flip that produces a silently wrong circuit, and it would make
    /// resolution depend on the alias list's contents — adding an alias for one kind could change how a
    /// script naming a <em>different</em> kind resolves.
    /// </value>
    public const double AmbiguityMargin = 0.05;

    /// <summary>The score below which a failed match carries no suggestion at all.</summary>
    /// <value>
    /// 0.60 — three characters in five. <c>15</c> fixes the resolve threshold and the ambiguity margin
    /// but never said when a suggestion stops being worth making, and <c>FS1502</c>'s message assumes
    /// one exists.
    /// </value>
    /// <remarks>
    /// The case that sets it is <c>fan</c>, which is two edits from <c>tank</c> in four characters and
    /// scores exactly 0.50. <c>D-28</c> wants an air-side kind to fail clearly rather than be nudged
    /// toward a hydronic one, and "there is no 'fan'. Did you mean 'tank'?" is worse than saying only
    /// that there is no <c>fan</c>. Above the floor a suggestion is still useful: <c>exchan</c> scores
    /// 0.67 against <c>exchanger</c>, too far to resolve and plainly aimed at it.
    /// </remarks>
    public const double SuggestionFloor = 0.60;

    /// <summary>Reduces a written name to the form the registry is indexed by.</summary>
    /// <param name="written">The name as the user wrote it.</param>
    /// <returns>Lowercase, with every underscore and space removed.</returns>
    /// <remarks>
    /// So <c>three_way_valve</c>, <c>ThreeWayValve</c>, <c>THREE_WAY_VALVE</c> and
    /// <c>three way valve</c> all become <c>threewayvalve</c>. Hyphens are not handled, because they
    /// never reach here: <c>-</c> is an operator, and <c>3-way-valve</c> fails in the lexer with
    /// <c>FS1108</c>, which suggests the underscored form directly.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="written"/> is <see langword="null"/>.</exception>
    public static string Normalize(string written)
    {
        ArgumentNullException.ThrowIfNull(written);

        var length = 0;
        var buffer = written.Length <= 64 ? stackalloc char[64] : new char[written.Length];

        foreach (var character in written)
        {
            if (character is '_' or ' ')
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..length]);
    }

    /// <summary>Scores how alike two normalised names are.</summary>
    /// <param name="a">One normalised name.</param>
    /// <param name="b">The other normalised name.</param>
    /// <returns>
    /// <c>1 − damerau_levenshtein(a, b) / max(len(a), len(b))</c>, so 1.0 for identical names and 0.0
    /// for names sharing nothing. Two empty names score 1.0.
    /// </returns>
    /// <remarks>
    /// Damerau rather than plain Levenshtein because a transposition is what a typing user actually
    /// produces: <c>pmup</c> for <c>pump</c> is one keystroke out of order, not two substitutions.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static double Score(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var longest = Math.Max(a.Length, b.Length);
        if (longest == 0)
        {
            return 1.0;
        }

        return 1.0 - ((double)Distance(a, b) / longest);
    }

    /// <summary>Ranks a written name against a set of candidates, exact matches first.</summary>
    /// <typeparam name="T">What a candidate resolves to.</typeparam>
    /// <param name="written">The name as the user wrote it.</param>
    /// <param name="index">Normalised spelling to candidate, including every alias.</param>
    /// <returns>
    /// The exact hit if there is one, otherwise the best scoring candidate and its runner-up, so the
    /// caller can apply <see cref="ResolveThreshold"/> and <see cref="AmbiguityMargin"/> in the terms
    /// of its own diagnostics.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static NameMatch<T> Match<T>(string written, ImmutableDictionary<string, T> index)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(index);

        var normalized = Normalize(written);
        if (index.TryGetValue(normalized, out var exact))
        {
            return new NameMatch<T>(exact, 1.0, null, 0.0, IsExact: true);
        }

        // Each candidate scores once, at its best spelling. A kind reached through two of its own
        // aliases is one candidate, not two — otherwise the kinds with the most aliases would be the
        // hardest to resolve, being perpetually ambiguous with themselves. Ranking as a list rather
        // than by carrying a best and a runner-up through the loop is what makes that correct: a
        // runner-up met again through a closer alias has to be able to improve its own score, and
        // keeping it in two variables silently kept the first score it was seen with.
        var ranked = new List<(T Candidate, string Spelling, double Score)>();

        foreach (var (spelling, candidate) in index)
        {
            var score = Score(normalized, spelling);
            var existing = ranked.FindIndex(entry => ReferenceEquals(entry.Candidate, candidate));

            if (existing < 0)
            {
                ranked.Add((candidate, spelling, score));
            }
            else if (score > ranked[existing].Score)
            {
                ranked[existing] = (candidate, spelling, score);
            }
        }

        // Ties broken by spelling so two candidates an equal distance away are always reported in the
        // same order, whatever order the index enumerated in.
        var order = ranked
            .OrderByDescending(static entry => entry.Score)
            .ThenBy(static entry => entry.Spelling, StringComparer.Ordinal)
            .ToArray();

        return order.Length switch
        {
            0 => new NameMatch<T>(null, 0.0, null, 0.0, IsExact: false),
            1 => new NameMatch<T>(order[0].Candidate, order[0].Score, null, 0.0, IsExact: false),
            _ => new NameMatch<T>(
                order[0].Candidate,
                order[0].Score,
                order[1].Candidate,
                order[1].Score,
                IsExact: false),
        };
    }

    // Damerau-Levenshtein with adjacent transposition, over two rolling rows plus the one before them.
    // The full matrix would be clearer and is not worth it: this runs against every alias in the
    // registry on every unresolved word, and an unresolved word is the normal state of a line being
    // typed.
    private static int Distance(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return Math.Max(a.Length, b.Length);
        }

        var previousPrevious = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = a[i - 1] == b[j - 1] ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitution);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    current[j] = Math.Min(current[j], previousPrevious[j - 2] + 1);
                }
            }

            (previousPrevious, previous, current) = (previous, current, previousPrevious);
        }

        return previous[b.Length];
    }
}

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
