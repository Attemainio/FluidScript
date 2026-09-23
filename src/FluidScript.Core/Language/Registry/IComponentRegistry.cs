using System.Collections.Immutable;

namespace FluidScript.Core.Language.Registry;

/// <summary>What the binder asks about component kinds.</summary>
public interface IComponentRegistry
{
    /// <summary>Gets every registered kind, in canonical keyword order.</summary>
    ImmutableArray<ComponentKindInfo> Kinds { get; }

    /// <summary>Resolves a kind name as the user wrote it.</summary>
    /// <param name="writtenKind">The name in <c>kind-name</c> position.</param>
    /// <returns>What it resolved to, which is never an exception and never a fabricated kind.</returns>
    KindResolution Resolve(string writtenKind);
}
