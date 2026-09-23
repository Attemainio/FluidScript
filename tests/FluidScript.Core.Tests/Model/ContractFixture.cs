using FluidScript.Core.Catalogs.Pipes;
using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Registry;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Model;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Solvers.Passes;
using FluidScript.Core.Solvers.Steady;
using FluidScript.Fixtures;

namespace FluidScript.Core.Tests.Model;

/// <summary>Runs the pipeline the way the API will, and hands the contract builder what it produced.</summary>
/// <remarks>
/// Parse, bind, prepare (which lowers with sizing's bootstrap, the graph nobody else builds --
/// <see cref="Topology.GraphFixture.Lower"/> says why), and optionally solve. Every diagnostic from
/// every stage is collected, because the contract's <c>diagnostics</c> is the one collection.
/// </remarks>
public static class ContractFixture
{
    /// <summary>The compile-only path: what the debounce sends while the user types.</summary>
    public static ModelContractInput Compile(string source)
    {
        var (parse, bind, catalog) = Front(source);
        var prepared = Loop(catalog).Prepare(bind.Model, Water.Instance);

        return new ModelContractInput
        {
            Source = parse.Source,
            Root = parse.Root,
            Model = bind.Model,
            Graph = prepared.Lowered.Graph,
            Run = null,
            Diagnostics = [.. parse.Diagnostics, .. bind.Diagnostics],
            Catalog = catalog.Catalog,
        };
    }

    /// <summary>The solved path.</summary>
    public static async Task<ModelContractInput> SolveAsync(string source, string name = "model")
    {
        var (parse, bind, catalog) = Front(source);
        var run = await Loop(catalog).RunAsync(bind.Model, Water.Instance, name, TestContext.Current.CancellationToken);

        Assert.True(run.IsSuccess, run.Error?.Message);

        return new ModelContractInput
        {
            Source = parse.Source,
            Root = parse.Root,
            Model = bind.Model,
            Graph = run.Value.Graph,
            Run = run.Value,
            Diagnostics = [.. parse.Diagnostics, .. bind.Diagnostics, .. run.Value.Solve.Diagnostics],
            Catalog = catalog.Catalog,
        };
    }

    public static string Sample(string name) => File.ReadAllText(Path.Combine(RepositoryLayout.Samples, name));

    private static (ParseResult Parse, BindResult Bind, ResolvedCatalog<PipeSpec> Catalog) Front(string source)
    {
        var parse = FluidScriptParser.Parse(new SourceText(source));
        var bind = new Binder(ComponentRegistry.Default).Bind(parse, "script");
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return (parse, bind, resolved.Value);
    }

    private static OuterLoop Loop(ResolvedCatalog<PipeSpec> catalog) =>
        new(new NewtonSolver(), new CatalogBoreLookup(catalog), OuterLoop.Rules(catalog.Catalog), 10);
}
