namespace FluidScript.Core.Layout;

/// <summary>One connection between two graph components, as the layout draws it.</summary>
/// <param name="Id">The route id: <c>c{n}</c> for the n-th connection the script wrote, <c>{pipe}#c{k}</c> for a link between an expanded pipe's cells (<c>C-124</c>).</param>
/// <param name="From">The graph index of the end the connection was written from.</param>
/// <param name="FromPort">Its port index.</param>
/// <param name="To">The graph index of the other end.</param>
/// <param name="ToPort">Its port index.</param>
internal readonly record struct GraphLink(string Id, int From, int FromPort, int To, int ToPort);
