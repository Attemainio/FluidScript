using FluidScript.Core.Language.Binding;
using FluidScript.Core.Language.Binding.Symbols;
using FluidScript.Core.Language.Syntax.Ast.Statements;
using FluidScript.Core.Language.Syntax.Parsing;
using FluidScript.Core.Language.Syntax.Printing;
using FluidScript.Core.Language.Syntax.Text;
using FluidScript.Core.Model;
using FluidScript.Core.Tests.Model;
using FluidScript.Core.Tests.Topology;

namespace FluidScript.Core.Tests.Language.Binding;

/// <summary>
/// Pipe properties on a connection line (<c>D-110</c>, P5.1e): the line parses and prints byte for byte, each
/// connection on it binds to an implicit pipe (rule I7) named after its ends, the nodes beside the pipe are named
/// after its ports, a length the line does not state is zero, and the circuit solves.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ImplicitPipeTests
{
    private const string Loop = """
        fluidscript 1
        circuit loop 100
        PU1 pump
        HE1 heat_exchanger power=30 in.t=20 out.t=50
        LOAD heat_exchanger power=-30
        connections
        N1 - PU1 - N2 - HE1 - N3 - LOAD - N4
        N4 - N1 dn=25 length=12
        N1 node p=250

        """;

    [Fact]
    public void AConnectionLineMayEndInPipePropertiesAndPrintsByteForByte()
    {
        var source = "fluidscript 1\nconnections\nN1 - HE1 - N2 dn=25 length=12 # the loop's return\n";
        var result = FluidScriptParser.Parse(new SourceText(source));

        Assert.Empty(result.Diagnostics);
        var connection = Assert.IsType<ConnectionSyntax>(result.Root.Statements[2]);
        Assert.Equal(["N1", "HE1", "N2"], connection.Endpoints.Select(static e => e.Component.Text));
        Assert.Equal(["dn", "length"], connection.Parameters.Select(static p => p.Name.Text));
        Assert.Equal(source, SyntaxPrinter.Print(result));
    }

    [Fact]
    public void APropertyWithoutAValueMakesTheLineMalformed()
    {
        var result = FluidScriptParser.Parse(new SourceText("fluidscript 1\nconnections\nN1 - HE1 dn\n"));

        Assert.IsType<MalformedStatementSyntax>(result.Root.Statements[2]);
        Assert.Contains(result.Diagnostics, static d => d.Code == "FS1105");
    }

    [Fact]
    public void AConnectionCarryingPropertiesBindsToAnImplicitPipeBetweenItsEnds()
    {
        var model = GraphFixture.Bind(Loop);
        var pipe = Assert.Single(model.Components, static c => c.Name == "N4__N1");

        Assert.Equal("pipe", pipe.Kind?.Keyword);
        Assert.True(pipe.Origin is Origin.Inferred { Rule: "I7" }, pipe.Origin.ToString());
        Assert.Equal(25, pipe.Parameters["dn"].Value!.Value.SiValue);
        Assert.Equal(12, pipe.Parameters["length"].Value!.Value.SiValue);
        Assert.Contains(model.Connections, static c => Link(c) == "N4.-N4__N1.in");
        Assert.Contains(model.Connections, static c => Link(c) == "N4__N1.out-N1.");
        Assert.DoesNotContain(model.Connections, static c => Link(c) == "N4.-N1.");
    }

    [Fact]
    public void ANodeBesideAnImplicitPipeIsNamedAfterThePipesPort()
    {
        // Two components joined through a pipe on the line: the pipe, then I2's node on each side of it.
        var model = GraphFixture.Bind("fluidscript 1\nHE1 heat_exchanger power=30\nPU1 pump\nconnections\nHE1 - PU1 dn=25\n");

        Assert.Contains(model.Components, static c => c.Name == "HE1__PU1" && c.Kind?.Keyword == "pipe");
        Assert.Contains(model.Components, static c => c.Name == "HE1__PU1__in" && c.Origin is Origin.Inferred { Rule: "I2" });
        Assert.Contains(model.Components, static c => c.Name == "HE1__PU1__out" && c.Origin is Origin.Inferred { Rule: "I2" });
        Assert.Contains(model.Connections, static c => Link(c) == "HE1.out-HE1__PU1__in.");
        Assert.Contains(model.Connections, static c => Link(c) == "HE1__PU1__in.-HE1__PU1.in");
        Assert.Contains(model.Connections, static c => Link(c) == "HE1__PU1.out-HE1__PU1__out.");
        Assert.Contains(model.Connections, static c => Link(c) == "HE1__PU1__out.-PU1.in");
    }

    [Fact]
    public void ThePropertiesApplyToEveryConnectionOnTheLine()
    {
        var model = GraphFixture.Bind("fluidscript 1\nconnections\nN1 - N2 - N3 dn=25\n");

        Assert.Contains(model.Components, static c => c.Name == "N1__N2" && c.Kind?.Keyword == "pipe");
        Assert.Contains(model.Components, static c => c.Name == "N2__N3" && c.Kind?.Keyword == "pipe");
    }

    [Fact]
    public async Task ALengthTheLineDoesNotStateIsZeroAndTheCircuitSolves()
    {
        // D-110: `dn=25` alone marks the drawing and the bore; the pipe drops nothing until a length is written.
        var source = Loop.Replace("N4 - N1 dn=25 length=12", "N4 - N1 dn=25", StringComparison.Ordinal);
        var solved = await ContractFixture.SolveAsync(source);
        var contract = ModelContractBuilder.Build(solved);
        var pipe = contract.Components.Single(static c => c.Id == "N4__N1");

        Assert.Equal("inferred:I7", pipe.Origin);
        Assert.Equal("stated", pipe.Parameters["dn"].Source);
        Assert.Equal("default", pipe.Parameters["length"].Source);
        Assert.Equal(0, pipe.Parameters["length"].Value);
        Assert.True(solved.Run!.Solve.Converged, solved.Run.Solve.Termination.ToString());
    }

    [Fact]
    public async Task AStatedLengthOnTheLineDropsPressureLikeADeclaredPipe()
    {
        var declared = Loop
            .Replace("LOAD heat_exchanger power=-30", "LOAD heat_exchanger power=-30\nP1 pipe dn=25 length=12", StringComparison.Ordinal)
            .Replace("N4 - N1 dn=25 length=12", "N4 - P1 - N1", StringComparison.Ordinal);

        var line = ModelContractBuilder.Build(await ContractFixture.SolveAsync(Loop));
        var component = ModelContractBuilder.Build(await ContractFixture.SolveAsync(declared));

        var implicitDrop = line.Components.Single(static c => c.Id == "N4__N1").State!.Dp!.Value;
        var declaredDrop = component.Components.Single(static c => c.Id == "P1").State!.Dp!.Value;
        Assert.NotNull(implicitDrop);
        Assert.NotNull(declaredDrop);
        Assert.True(implicitDrop > 0, "the implicit pipe drops nothing");
        Assert.Equal(declaredDrop.Value, implicitDrop.Value, 6);
    }

    private static string Link(ConnectionSymbol connection) =>
        $"{connection.From.Component}.{connection.From.Port}-{connection.To.Component}.{connection.To.Port}";

    [Fact]
    public void AnImplicitPipeCarriesItsConnectionLineAsItsSpanAndAnInferredNodeNone()
    {
        // C-97: the user wrote the line, so a click on the drawn pipe can land on it; an I2 node nobody
        // wrote still has no span. The symbol map still answers the connection at that line (54).
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(Loop));
        var pipe = contract.Components.Single(static c => c.Id == "N4__N1");
        var line = Loop.IndexOf("N4 - N1 dn=25 length=12", StringComparison.Ordinal);

        Assert.NotNull(pipe.SourceSpan);
        Assert.Equal(line, pipe.SourceSpan.Start);
        Assert.Equal("N4 - N1 dn=25 length=12".Length, pipe.SourceSpan.Length);
        Assert.Null(contract.Components.Single(static c => c.Id == "N2").SourceSpan);

        var model = GraphFixture.Bind(Loop);
        Assert.IsType<SymbolReference.Connection>(model.SymbolMap.AtOffset(line + "N4 - N1 dn=".Length));
    }

    [Fact]
    public void ADiagnosticOnAConnectionLineNamesTheComponentItIsAboutNotThePipeThatOwnsTheLine()
    {
        // L-55: the wire's `component` is what the badge, the card and the log key on. An I2 node's
        // FS1510 and an I7 pipe's FS1510 are both raised on the same connection line; each carries
        // the component it names, and the span fallback (a declared component's line) no longer hands
        // the node's notice to the pipe because the pipe now owns the line (C-97).
        var contract = ModelContractBuilder.Build(ContractFixture.Compile(Loop));
        var added = contract.Diagnostics.Where(static d => d.Code == "FS1510").ToList();

        var pipe = Assert.Single(added, static d => d.Message.Contains("pipe 'N4__N1'", StringComparison.Ordinal));
        Assert.Equal("N4__N1", pipe.Component);

        foreach (var node in added.Where(static d => d.Message.Contains("node '", StringComparison.Ordinal)))
        {
            var name = node.Message.Split('\'')[1];
            Assert.Equal(name, node.Component);
        }
    }
}
