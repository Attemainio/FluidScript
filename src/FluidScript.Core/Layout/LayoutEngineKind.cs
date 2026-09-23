namespace FluidScript.Core.Layout;

/// <summary>Which layout engine draws a scene while the rebuilt one is brought to parity (<c>D-153</c>, <c>28</c> part E).</summary>
public enum LayoutEngineKind
{
    /// <summary>The engine the ladder built rule by rule (<c>D-106</c>, <c>D-107</c>); the default until the switch.</summary>
    Ladder,

    /// <summary>The rebuilt engine: decompose, compose, draw (<c>D-153</c>).</summary>
    Composed,
}
