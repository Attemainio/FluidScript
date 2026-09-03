using System.Collections.Immutable;
using System.Text.RegularExpressions;

using FluidScript.Core.Diagnostics;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Diagnostics;

/// <summary>
/// Every registered diagnostic code is named by some test, or carries a row saying why it is not.
/// </summary>
/// <remarks>
/// <para>
/// <c>CodeRangeOwnershipTests</c> checks both directions of the <em>documentation</em> claim — every
/// code sits in a range, and the document that range names says what it means — and neither direction
/// of the behavioural one. Two failures came through that gap from opposite sides. <c>FS2211</c> was
/// implemented and permanently unreachable for a whole package, because the equation set was missing a
/// row (<c>S-8</c>, <c>D-65</c>); <c>FS2202</c> and <c>FS2217</c> shipped reachable with no test naming
/// either. Both are one failure: nothing ever asked whether a code was exercised.
/// </para>
/// <para>
/// <strong>This is a floor, not a proof.</strong> "Named by a test" is a text scan for the code as a
/// string literal, so a mention in a negative assertion — <c>Assert.DoesNotContain("FS1518", …)</c> —
/// counts, and so does one in a test asserting something other than the code firing. What it catches
/// is the failure that actually happened twice: a live code no test mentions anywhere. Proving a code
/// is <em>raised</em> means observing the suite emit it, which needs a collector every test routes
/// through — and a gate that invasive is one somebody eventually turns off.
/// </para>
/// <para>
/// Three kinds of occurrence deliberately do not count. <strong>Comments are stripped</strong>, because
/// this codebase cites codes in prose constantly and <c>FS2217</c>'s own emit site carries a comment
/// naming it — which would have covered the untested code for free. <strong>A descriptor constructed
/// in a fixture</strong> is a code defined, not exercised: <c>DiagnosticDescriptorTests</c> builds an
/// <c>FS1201</c> to check message formatting, and if <c>L-1</c> ever registers that code, the fixture
/// must not be what covers it. And <strong>this file is excluded from its own scan</strong>, without
/// which an <see cref="Uncovered"/> row would name the code it exempts and immediately cover it.
/// </para>
/// </remarks>
public sealed partial class DiagnosticCoverageTests
{
    /// <summary>A registered code no test exercises, and the reason that is acceptable for now.</summary>
    /// <param name="Code">The registered code.</param>
    /// <param name="Defect">The defect id tracking it, so the exemption has an owner and an end.</param>
    /// <param name="Reason">Why nothing can exercise it yet.</param>
    private readonly record struct Uncovered(string Code, string Defect, string Reason);

    /// <summary>The codes exempt from needing a test.</summary>
    /// <remarks>
    /// <para>
    /// Empty, and that is the result rather than the design: all 97 registered codes are named by a
    /// test today, and every one of them has an emit site.
    /// </para>
    /// <para>
    /// The rows exist for the shape the registry deliberately has. <c>DiagnosticRegistry</c> grows one
    /// package at a time precisely so that "every entry is emitted by some path" stays satisfiable, but
    /// a package that registers a code ahead of the stage which can <em>raise</em> it has nowhere to
    /// say so. A row here is that place. It is not a suppression: the defect id is required, and
    /// <see cref="NoExemptionSurvivesTheTestThatCoversIt"/> deletes the row the moment a test covers
    /// the code.
    /// </para>
    /// </remarks>
    private static readonly ImmutableArray<Uncovered> Exempt = [];

    private static readonly Lazy<ImmutableArray<string>> LazyNamed = new(Named);

    [GeneratedRegex("\"(FS[0-9]{4})\"")]
    private static partial Regex CodeLiteral();

    [GeneratedRegex("\"FS[0-9]{4}\",\\s*DiagnosticSeverity\\.")]
    private static partial Regex FixtureDescriptor();

    [Fact]
    [Trait("Category", "Unit")]
    public void TheScanActuallyFindsCodes()
    {
        // A pattern that matches nothing makes every assertion below pass while checking nothing --
        // the failure mode of every drift check read out of source text. Ninety-seven codes are
        // registered today; the floor is far enough under that registering more does not fail this,
        // and far enough over zero that a broken pattern does.
        Assert.True(
            LazyNamed.Value.Length >= 80,
            $"The test tree scanned as {LazyNamed.Value.Length} distinct codes, which means the "
            + "pattern stopped matching rather than the suite having shrunk that far.");

        Assert.Contains("FS1501", LazyNamed.Value);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryRegisteredCodeIsNamedByATest()
    {
        var named = LazyNamed.Value.ToHashSet(StringComparer.Ordinal);
        var exempt = Exempt.Select(static entry => entry.Code).ToHashSet(StringComparer.Ordinal);

        var uncovered = DiagnosticRegistry.All
            .Select(static descriptor => descriptor.Code)
            .Where(code => !named.Contains(code) && !exempt.Contains(code))
            .ToArray();

        Assert.True(
            uncovered.Length == 0,
            "A code no test names is a code nothing has watched fire. Write the test, or add a row to "
            + $"{nameof(Exempt)} naming the defect that tracks it: {string.Join(", ", uncovered)}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NoExemptionSurvivesTheTestThatCoversIt()
    {
        // The direction that keeps the list from becoming a graveyard. An exemption is a claim that
        // nothing exercises the code; the moment that stops being true the row is misinformation, and
        // the next reader takes it as evidence the code is still unreachable.
        var named = LazyNamed.Value.ToHashSet(StringComparer.Ordinal);
        var stale = Exempt.Where(entry => named.Contains(entry.Code)).ToArray();

        Assert.True(
            stale.Length == 0,
            "An exemption claims nothing tests the code. These are tested, so the rows go: "
            + string.Join(", ", stale.Select(static entry => $"{entry.Code} ({entry.Defect})")));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void EveryExemptionNamesARegisteredCodeAndADefect()
    {
        var registered = DiagnosticRegistry.All
            .Select(static descriptor => descriptor.Code)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in Exempt)
        {
            Assert.True(
                registered.Contains(entry.Code),
                $"{entry.Code} is exempted from a rule it is not subject to: it is not registered.");

            Assert.False(
                string.IsNullOrWhiteSpace(entry.Defect),
                $"{entry.Code}'s exemption names no defect, so nothing will ever remove it.");

            Assert.False(
                string.IsNullOrWhiteSpace(entry.Reason),
                $"{entry.Code}'s exemption gives no reason, which is a suppression wearing a record.");
        }
    }

    /// <summary>Every diagnostic code named by a string literal anywhere under <c>tests/</c>.</summary>
    /// <returns>The distinct codes, ordered, so a failure message reads the same on every platform.</returns>
    private static ImmutableArray<string> Named()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in RepositoryLayout.EnumerateSourceFiles())
        {
            if (!path.StartsWith(RepositoryLayout.Tests, StringComparison.Ordinal)
                || Path.GetFileName(path).Equals(Self, StringComparison.Ordinal))
            {
                continue;
            }

            var text = FixtureDescriptor().Replace(WithoutComments(File.ReadAllText(path)), string.Empty);

            foreach (var match in CodeLiteral().Matches(text).Cast<Match>())
            {
                found.Add(match.Groups[1].Value);
            }
        }

        return [.. found];
    }

    private const string Self = "DiagnosticCoverageTests.cs";

    /// <summary>Removes line comments, leaving string literals intact.</summary>
    /// <param name="text">A C# source file.</param>
    /// <returns>The same text with everything after an uncommented <c>//</c> on each line removed.</returns>
    /// <remarks>
    /// Line by line, and a <c>//</c> opens a comment only where an even number of quotes precedes it on
    /// its own line. That is not a C# lexer and does not need to be: it has one job, which is to stop a
    /// code cited in prose from counting as a code under test. A code inside a multi-line raw string
    /// literal would defeat it, and none exists.
    /// </remarks>
    private static string WithoutComments(string text)
    {
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            for (var at = line.IndexOf("//", StringComparison.Ordinal);
                 at >= 0;
                 at = line.IndexOf("//", at + 1, StringComparison.Ordinal))
            {
                if (line.AsSpan(0, at).Count('"') % 2 == 0)
                {
                    lines[i] = line[..at];
                    break;
                }
            }
        }

        return string.Join('\n', lines);
    }
}
