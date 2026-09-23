using FluidScript.Core.Language.Registry;

namespace FluidScript.Core.Language.Binding;

/// <summary>Matches an indexed family pattern such as <c>t{index}</c> against a written name.</summary>
internal static class Indexed
{
    /// <summary>Tells whether a written name is a member of a pattern's family, and which one.</summary>
    /// <param name="pattern">The canonical pattern, with one <c>{index}</c> placeholder.</param>
    /// <param name="written">The name to test.</param>
    /// <param name="index">The index it carries, or zero when it is not a member.</param>
    /// <returns><see langword="true"/> when the name matches the pattern.</returns>
    /// <remarks>
    /// A forwarder, kept because the parameter path reads better calling <c>Indexed.Matches</c> in a
    /// file about binding. The rule itself moved to the registry when property families needed it
    /// too: <see cref="ComponentKindInfo.ResolveProperty(string)"/> is read by the model contract as well as
    /// by this class, and two copies of the pattern rule is one place for the two halves to diverge.
    /// </remarks>
    public static bool Matches(string pattern, string written, out int index) =>
        IndexedName.Matches(pattern, written, out index);
}
