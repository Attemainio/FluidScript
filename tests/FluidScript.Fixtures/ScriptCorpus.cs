using System.Collections.Immutable;

namespace FluidScript.Fixtures;

/// <summary>Every piece of FluidScript source the repository contains.</summary>
/// <remarks>
/// <para>
/// Two sources, and both matter. <c>samples/</c> holds whole scripts that are meant to be valid.
/// <c>plan/</c> and <c>docs/</c> hold hundreds of fenced <c>fluidscript</c> blocks, many of them
/// fragments and some deliberately wrong, and those are what catch a specification that contradicts
/// itself: <c>plan/10-language/12-grammar.md</c> records that both reference circuits once failed to
/// parse while every acceptance criterion in that document passed.
/// </para>
/// <para>
/// A block is used for the properties that hold for any text at all — losslessness, termination, spans
/// inside bounds — and for parsing cleanly. <c>61</c> requires every documented example to be complete
/// and runnable, and <c>12</c>'s acceptance criterion covers the plan's blocks too, so a block that is
/// meant to be wrong says so on its fence: <c>```fluidscript expects=FS1203</c>. A block that is not a
/// script at all — an editing session with a cursor in it, say — is not marked <c>fluidscript</c>.
/// </para>
/// </remarks>
public static class ScriptCorpus
{
    private const string Fence = "```";
    private const string Language = "fluidscript";

    /// <summary>Enumerates the sample scripts.</summary>
    /// <returns>
    /// Absolute paths to every <c>.fluid</c> file under <c>samples/</c>, ordered by path so a failure
    /// names the same file on every platform.
    /// </returns>
    public static IEnumerable<string> EnumerateSampleFiles() =>
        Directory.Exists(RepositoryLayout.Samples)
            ? Directory.EnumerateFiles(RepositoryLayout.Samples, "*.fluid", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
            : [];

    /// <summary>Reads every sample script.</summary>
    /// <returns>One entry per file, carrying the path for a failure message and the text verbatim.</returns>
    /// <remarks>
    /// Read once per test run and cached. Nothing writes to <c>samples/</c> during a run, and the
    /// corpus is walked by dozens of tests: re-reading it each time cost more than everything the
    /// suite actually measures, against <c>08</c>'s two-second budget for the unit tier.
    /// </remarks>
    public static ImmutableArray<ScriptSource> Samples() => LazySamples.Value;

    private static readonly Lazy<ImmutableArray<ScriptSource>> LazySamples = new(() =>
    [
        .. EnumerateSampleFiles()
            .Select(static path => new ScriptSource(RepositoryLayout.ToRelative(path), ReadVerbatim(path), [])),
    ]);

    /// <summary>Extracts every fenced <c>fluidscript</c> block from the plan and the documentation.</summary>
    /// <returns>
    /// One entry per block, named by file and the line its fence opens on, in file then line order.
    /// The text is the block's content without the fences, and without a trailing newline where the
    /// fence supplied one.
    /// </returns>
    /// <remarks>Read once per test run and cached, as <see cref="Samples"/> is.</remarks>
    public static ImmutableArray<ScriptSource> MarkdownBlocks() => LazyBlocks.Value;

    private static readonly Lazy<ImmutableArray<ScriptSource>> LazyBlocks = new(ReadMarkdownBlocks);

    private static ImmutableArray<ScriptSource> ReadMarkdownBlocks()
    {
        var blocks = ImmutableArray.CreateBuilder<ScriptSource>();

        foreach (var directory in new[] { "plan", "docs" })
        {
            var root = Path.Combine(RepositoryLayout.Root, directory);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                blocks.AddRange(BlocksIn(path));
            }
        }

        return blocks.ToImmutable();
    }

    /// <summary>Everything in the corpus: samples first, then markdown blocks.</summary>
    /// <returns>The concatenation of <see cref="Samples"/> and <see cref="MarkdownBlocks"/>.</returns>
    public static ImmutableArray<ScriptSource> All() => [.. Samples(), .. MarkdownBlocks()];

    private static IEnumerable<ScriptSource> BlocksIn(string path)
    {
        var relative = RepositoryLayout.ToRelative(path);
        var lines = ReadVerbatim(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (!TryOpenFence(lines[i], out var expected))
            {
                continue;
            }

            var opened = i;
            var content = new List<string>();
            i++;

            while (i < lines.Length && !lines[i].TrimEnd().Equals(Fence, StringComparison.Ordinal))
            {
                content.Add(lines[i]);
                i++;
            }

            // A block whose fence is never closed is a defect in the markdown rather than a script,
            // and it is reported by whatever renders the page. Skipping it keeps this from turning
            // the rest of the file into one enormous fake script.
            if (i < lines.Length)
            {
                yield return new ScriptSource(
                    $"{relative}:{opened + 1}",
                    string.Join('\n', content) + "\n",
                    expected);
            }
        }
    }

    private static bool TryOpenFence(string line, out ImmutableArray<string> expected)
    {
        expected = [];

        var text = line.TrimEnd();
        if (!text.StartsWith(Fence + Language, StringComparison.Ordinal))
        {
            return false;
        }

        var info = text[(Fence.Length + Language.Length)..].Trim();
        if (info.Length == 0)
        {
            return true;
        }

        // The one annotation the corpus reads, and the reason the info line is not simply compared:
        // `61` requires an intentionally-broken example to be annotated with what it produces. Any
        // other info string is not a fluidscript block, which is what keeps ```fluidscriptish out.
        const string Expects = "expects=";
        if (!info.StartsWith(Expects, StringComparison.Ordinal))
        {
            return false;
        }

        expected =
        [
            .. info[Expects.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

        return true;
    }

    // File.ReadAllText strips nothing, but it does detect and consume a byte-order mark. Reading the
    // bytes and decoding without one keeps a BOM in the text, where a losslessness assertion has to
    // account for it -- which is the honest test, since that is what an editor would send.
    private static string ReadVerbatim(string path) =>
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(File.ReadAllBytes(path));

    /// <summary>Text that is not a script, chosen for the ways a scanner fails to terminate.</summary>
    /// <value>
    /// Short strings, each one a shape that has broken a scanner somewhere: unterminated constructs,
    /// the dot cases <c>D-51</c> settled, an exponent with no digits, a byte-order mark, an emoji.
    /// </value>
    public static ImmutableArray<string> Adversarial { get; } =
    [
        "",
        " ",
        "\n",
        "\r",
        "\r\n",
        "\n\r",
        "#",
        "#no newline at end",
        "\"",
        "\"unterminated",
        "\"unterminated\nnext line",
        ".",
        "..",
        "...",
        "30.",
        "30..60",
        "30...60",
        "1e",
        "1e+",
        "1e-",
        "1exchanger",
        "0x1F",
        "%",
        "10 % 3",
        "é",
        "\U0001F600",
        "﻿fluidscript 1",
        "let x = ",
        "= = =",
        "((((((((((",
        "3WV",
        "30 ",
        "30 kW",
        "30kW",
        "\t\t\t",
        "a b",
    ];

    /// <summary>Produces mutations of the sample scripts.</summary>
    /// <param name="count">How many mutations to produce.</param>
    /// <param name="seed">
    /// The random seed. Every caller fixes it: a fuzz that finds a different failure on each run
    /// cannot be bisected, and one that finds none is indistinguishable from one that is not running.
    /// </param>
    /// <returns>
    /// <paramref name="count"/> texts, each a sample with one to five characters deleted, inserted or
    /// replaced. Every intermediate state of a script being edited is reachable this way, which is
    /// what makes this the standing test for "no stage throws on user input" rather than a milestone
    /// check (<c>08</c>, P2.5).
    /// </returns>
    public static IEnumerable<string> Mutations(int count, int seed)
    {
        var random = new Random(seed);
        var seeds = Samples();

        for (var i = 0; i < count && seeds.Length > 0; i++)
        {
            yield return Mutate(seeds[random.Next(seeds.Length)].Text, random);
        }
    }

    // Weighted towards the characters that carry lexical decisions, because a uniform draw over the
    // whole code-point space spends nearly every mutation on a character the lexer treats identically.
    private static readonly ImmutableArray<char> Interesting =
        [.. "\"#=.-+*/%@,()\n\r\t 0123456789eE_kWmsK° é"];

    private static string Mutate(string text, Random random)
    {
        var builder = new System.Text.StringBuilder(text);
        var edits = random.Next(1, 6);

        for (var i = 0; i < edits && builder.Length > 0; i++)
        {
            var at = random.Next(builder.Length);
            switch (random.Next(3))
            {
                case 0:
                    builder.Remove(at, 1);
                    break;
                case 1:
                    builder.Insert(at, Interesting[random.Next(Interesting.Length)]);
                    break;
                default:
                    builder[at] = Interesting[random.Next(Interesting.Length)];
                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>One piece of FluidScript source, with a name that identifies it in a failure message.</summary>
/// <param name="Name">
/// A repository-relative path, and for a markdown block the line its fence opens on.
/// </param>
/// <param name="Text">The source verbatim.</param>
/// <param name="Expected">
/// The diagnostic codes the block's fence says it produces, empty for a block that is meant to be
/// clean. Written as <c>```fluidscript expects=FS1102,FS1104</c>.
/// </param>
public readonly record struct ScriptSource(string Name, string Text, ImmutableArray<string> Expected);
