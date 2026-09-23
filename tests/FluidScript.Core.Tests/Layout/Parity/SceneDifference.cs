using System.Globalization;
using System.Text;

using FluidScript.Core.Language.Binding;
using FluidScript.Core.Layout.Drawing;

namespace FluidScript.Core.Tests.Layout.Parity;

/// <summary>
/// The difference between the ladder engine's scene and the composed engine's for one script (<c>D-153</c>,
/// <c>28</c> E5): the audit counts of each, every placement that moved, turned, or is missing on one side, and every
/// route whose points, length or bends changed.
/// </summary>
/// <param name="Name">The case: a ladder step or a sample.</param>
/// <param name="Ladder">The ladder engine's scene.</param>
/// <param name="Composed">The composed engine's scene.</param>
/// <param name="Model">The model both were solved from, for the audit.</param>
internal sealed record SceneDifference(string Name, Scene Ladder, Scene Composed, SemanticModel Model)
{
    private const double Eps = 1e-6;

    /// <summary>Gets the ladder engine's hard and soft finding counts.</summary>
    public (int Hard, int Soft) LadderAudit => Count(Ladder);

    /// <summary>Gets the composed engine's hard and soft finding counts.</summary>
    public (int Hard, int Soft) ComposedAudit => Count(Composed);

    /// <summary>Gets how many of the ladder's placements the composed scene has at the same place, in the same transform.</summary>
    public int SamePlacements => Ladder.Placements.Count(p => Composed.Placements.Any(q => q.ComponentId == p.ComponentId && Same(p, q)));

    /// <summary>Gets how many of the ladder's routes the composed scene draws through the same points.</summary>
    public int SameRoutes => Ladder.Routes.Count(r => Composed.Routes.Any(s => s.ConnectionId == r.ConnectionId && SamePoints(r, s)));

    /// <summary>Gets the verdict: <c>identical</c>, <c>incomplete</c> when the composed scene lacks a placement or a route, else <c>differs</c>.</summary>
    public string Verdict =>
        SamePlacements == Ladder.Placements.Length && SameRoutes == Ladder.Routes.Length && Composed.Placements.Length == Ladder.Placements.Length && Composed.Routes.Length == Ladder.Routes.Length
            ? "identical"
            : Ladder.Placements.Any(p => Composed.Placements.All(q => q.ComponentId != p.ComponentId)) || Ladder.Routes.Any(r => Composed.Routes.All(s => s.ConnectionId != r.ConnectionId))
                ? "incomplete"
                : "differs";

    /// <summary>The summary line for this case.</summary>
    /// <returns>One line: the case, both audits, the placements and routes kept, the verdict.</returns>
    public string Line() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Name,-34} ladder hard {LadderAudit.Hard,3} soft {LadderAudit.Soft,3} | composed hard {ComposedAudit.Hard,3} soft {ComposedAudit.Soft,3} | placements {SamePlacements,3}/{Ladder.Placements.Length,-3} routes {SameRoutes,3}/{Ladder.Routes.Length,-3} | {Verdict}");

    /// <summary>The full report for this case.</summary>
    /// <returns>The summary line, then every placement and route that is not the same, one per line.</returns>
    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"PARITY {Name}");
        text.AppendLine(Line());
        text.AppendLine();
        text.AppendLine("PLACEMENTS (ladder -> composed)");

        foreach (var p in Ladder.Placements)
        {
            var q = Composed.Placements.FirstOrDefault(c => c.ComponentId == p.ComponentId);

            if (q is null)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $" {p.ComponentId}: missing (ladder at {Text(p.Inner.Centre)})");
            }
            else if (!Same(p, q))
            {
                var d = new Point(q.Inner.Centre.X - p.Inner.Centre.X, q.Inner.Centre.Y - p.Inner.Centre.Y);
                var turned = Transform(p) == Transform(q) ? string.Empty : $", {Transform(p)} -> {Transform(q)}";
                text.AppendLine(CultureInfo.InvariantCulture, $" {p.ComponentId}: moved {Text(d)} to {Text(q.Inner.Centre)}{turned}");
            }
        }

        foreach (var q in Composed.Placements.Where(q => Ladder.Placements.All(p => p.ComponentId != q.ComponentId)))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $" {q.ComponentId}: only in composed, at {Text(q.Inner.Centre)}");
        }

        text.AppendLine();
        text.AppendLine("ROUTES (ladder -> composed)");

        foreach (var r in Ladder.Routes)
        {
            var s = Composed.Routes.FirstOrDefault(c => c.ConnectionId == r.ConnectionId);

            if (s is null)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $" {r.ConnectionId}: missing");
            }
            else if (!SamePoints(r, s))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $" {r.ConnectionId}: length {N(r.Length)} -> {N(s.Length)}, bends {r.Bends.Count()} -> {s.Bends.Count()}; {Text(s.Points)}");
            }
        }

        foreach (var s in Composed.Routes.Where(s => Ladder.Routes.All(r => r.ConnectionId != s.ConnectionId)))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $" {s.ConnectionId}: only in composed; {Text(s.Points)}");
        }

        text.AppendLine();
        text.AppendLine("COMPOSED FINDINGS");

        foreach (var finding in SceneAudit.Findings(Composed, Model))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $" {finding}");
        }

        text.AppendLine();
        text.AppendLine("COMPOSED TRACE");

        foreach (var note in Composed.Provenance)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $" {note.Rule} -- {note.Subject}: {note.Reason}");
        }

        return text.ToString();
    }

    private (int Hard, int Soft) Count(Scene scene)
    {
        var findings = SceneAudit.Findings(scene, Model);
        return (findings.Count(static f => f.Hard), findings.Count(static f => !f.Hard));
    }

    private static bool Same(Placement p, Placement q) =>
        p.Inner.Centre.ManhattanTo(q.Inner.Centre) < Eps && Transform(p) == Transform(q);

    private static bool SamePoints(Route r, Route s) =>
        r.Points.Length == s.Points.Length && r.Points.Zip(s.Points).All(static pair => pair.First.ManhattanTo(pair.Second) < Eps);

    private static string Transform(Placement p) => $"{p.Arrangement} rot {p.Rotation}{(p.Mirrored ? " mirrored" : string.Empty)}";

    private static string Text(Point p) => $"({N(p.X)}, {N(p.Y)})";

    private static string Text(IEnumerable<Point> points) => string.Join(" ", points.Select(Text));

    private static string N(double v) => Math.Round(v, 6).ToString("0.###", CultureInfo.InvariantCulture);
}
