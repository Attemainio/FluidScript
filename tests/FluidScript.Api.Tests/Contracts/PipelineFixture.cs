using System.Collections.Immutable;

using FluidScript.Core.Binding;
using FluidScript.Core.Catalogs;
using FluidScript.Core.Fluids;
using FluidScript.Core.Language;
using FluidScript.Core.Model;
using FluidScript.Core.Solvers;
using FluidScript.Core.Syntax;
using FluidScript.Fixtures;

namespace FluidScript.Api.Tests.Contracts;

/// <summary>Runs the pipeline the way the host will, and hands the contract builder what it produced.</summary>
public static class PipelineFixture
{
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
            Catalog = catalog,
        };
    }

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
            Catalog = catalog,
        };
    }

    public static string Sample(string name) => File.ReadAllText(Path.Combine(RepositoryLayout.Samples, name));

    private static (ParseResult Parse, BindResult Bind, ICatalog<PipeSpec> Catalog) Front(string source)
    {
        var parse = FluidScriptParser.Parse(new SourceText(source));
        var bind = new Binder(ComponentRegistry.Default).Bind(parse, "script");
        var resolved = PipeCatalogs.Resolve(pin: null);

        Assert.True(resolved.IsSuccess, resolved.Error?.Message);

        return (parse, bind, resolved.Value.Catalog);
    }

    private static OuterLoop Loop(ICatalog<PipeSpec> catalog) =>
        new(new NewtonSolver(), new CatalogBoreLookup(catalog), OuterLoop.Rules(catalog), 10);
}
