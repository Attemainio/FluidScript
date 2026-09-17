using FluidScript.Core.Binding;
using FluidScript.Core.Diagnostics;
using FluidScript.Core.Language;
using FluidScript.Core.Syntax;

namespace FluidScript.Core.Tests.Binding;

/// <summary>
/// <c>D-36</c>: a component touching two circuits belongs to the one on the side <em>losing</em>
/// nominal enthalpy, for tagging and grouping. <c>P4.3</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The swap is the test.</strong> "The leftmost circuit owns it" is what a designer sees, and
/// leftmost is a layout outcome <c>D-03</c> forbids Core from knowing; "the circuit that declared it"
/// is trivial and makes a tag change when a line moves between blocks, which is what <c>D-34</c>
/// exists to prevent. Enthalpy direction is the same rule stated in Core's own terms, and its test is
/// that reordering the two circuit blocks in the source — or moving the exchanger's declaration from
/// one block to the other — changes nothing about the tag.
/// </para>
/// <para>
/// Ownership is a tagging and grouping question and never a solver one: no equation, unknown, datum
/// or balance reads it, which is why the fallback of the lower circuit number is safe as well as
/// deterministic, and why it is reported (<c>FS2216</c>) rather than silent.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class OwnershipTests
{
    /// <summary>The substation as two circuit blocks: the district primary 400 and the heating secondary 100.</summary>
    private const string District = """
        circuit district 400
        fluid water

        NPS inlet t=85 p=600
        NPR outlet p=350
        PCV valve
        PP  pipe length=12 dn=25
        {0}

        connections
        NPS - PCV - PP - HX1.in2
        HX1.out2 - NPR
        """;

    private const string Heating = """
        circuit heating 100
        fluid water

        SP   pump
        SS   pipe length=30 dn=32
        SR   pipe length=30 dn=32
        LOAD heat_exchanger power=-150 dt=20
        {1}

        connections
        HX1.out - SS - NSUP
        NSUP - LOAD - NRET
        NRET - SR - SP - HX1.in
        """;

    private const string Exchanger = "HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45 u=3300";

    private static string Script(bool districtFirst, bool declaredInDistrict, string exchanger = Exchanger)
    {
        var district = District.Replace("{0}", declaredInDistrict ? exchanger : string.Empty, StringComparison.Ordinal);
        var heating = Heating.Replace("{1}", declaredInDistrict ? string.Empty : exchanger, StringComparison.Ordinal);

        return "fluidscript 1\n" + (districtFirst ? district + "\n\n" + heating : heating + "\n\n" + district) + "\n";
    }

    private static BindResult Bind(string source) =>
        new Binder(ComponentRegistry.Default).Bind(FluidScriptParser.Parse(new SourceText(source)), "script");

    private static SemanticModel Model(string source)
    {
        var result = Bind(source);

        Assert.True(
            result.Diagnostics.All(static d => d.Severity != DiagnosticSeverity.Error),
            string.Join("; ", result.Diagnostics.Select(static d => $"{d.Code} {d.Message}")));

        return result.Model;
    }

    private static ComponentSymbol Exchanger1(SemanticModel model) =>
        model.Components.Single(static c => c.Name == "HX1");

    [Fact]
    public void TheExchangerBelongsToTheCircuitLosingEnthalpyWhicheverBlockDeclaresIt()
    {
        // 150 kW into side 1: the district primary on side 2 is the side losing enthalpy, so the tag is
        // 400HE01 -- whether HX1's line sits in the district block or the heating one.
        foreach (var declaredInDistrict in (bool[])[true, false])
        {
            var exchanger = Exchanger1(Model(Script(districtFirst: true, declaredInDistrict)));

            Assert.Equal("400HE01", exchanger.Tag);
            Assert.Equal("district", exchanger.CircuitName);
        }
    }

    [Fact]
    public void SwappingTheCircuitBlocksMovesNoTag()
    {
        // 05's criterion, verbatim: declaration order must not renumber equipment.
        var forward = Model(Script(districtFirst: true, declaredInDistrict: false));
        var swapped = Model(Script(districtFirst: false, declaredInDistrict: false));

        Assert.Equal("400HE01", Exchanger1(forward).Tag);
        Assert.Equal("400HE01", Exchanger1(swapped).Tag);
        Assert.Equal("100HE01", forward.Components.Single(static c => c.Name == "LOAD").Tag);
        Assert.Equal("100HE01", swapped.Components.Single(static c => c.Name == "LOAD").Tag);
    }

    [Fact]
    public void AConsumerOnSideOneIsOwnedByTheCircuitOnSideOne()
    {
        // The mirror image: `power=-150` means side 1 loses, so the heating circuit owns it -- and the
        // role word carries the same sign. A chiller cooling circuit 100 from circuit 400 is 100HE01.
        var signed = Exchanger1(Model(Script(true, true, "HX1 heat_exchanger power=-150 in=60 out=40 in2=45 out2=85")));
        var worded = Exchanger1(Model(Script(true, true, "HX1 chiller power=150 in=60 out=40 in2=45 out2=85")));

        Assert.Equal("100HE01", signed.Tag);
        Assert.Equal("100HE01", worded.Tag);
    }

    [Fact]
    public void WithoutADutyTheTerminalTemperaturesDecideTheDirection()
    {
        // No `power`: 85 -> 45 on side 2 is a drop, 40 -> 60 on side 1 a rise. Side 2 loses.
        var exchanger = Exchanger1(Model(Script(true, false, "HX1 heat_exchanger in=40 out=60 in2=85 out2=45")));

        Assert.Equal("400HE01", exchanger.Tag);
    }

    [Fact]
    public void FS2216_NoHeatDirectionFallsBackToTheLowerCircuitNumberAndSaysSo()
    {
        // Nothing states which way heat goes: no duty, no terminals. The lower number is deterministic
        // and safe -- nothing in the solve reads ownership -- and the diagram groups by circuit, so a
        // component that landed somewhere arbitrary says so.
        var source = Script(true, true, "HX1 heat_exchanger");
        var result = Bind(source);
        var reported = Assert.Single(result.Diagnostics, static d => d.Code == "FS2216");
        var span = Assert.NotNull(reported.Span);

        Assert.Equal(DiagnosticSeverity.Info, reported.Severity);
        Assert.StartsWith("HX1 heat_exchanger", source.Substring(span.Start, span.Length), StringComparison.Ordinal);
        Assert.Contains("heating", reported.Message, StringComparison.Ordinal);
        Assert.Equal("100HE01", Exchanger1(result.Model).Tag);
    }

    [Fact]
    public void BothSidesInOneCircuitIsThatCircuitWithNothingToReport()
    {
        var result = Bind("""
            fluidscript 1
            circuit pair 300
            fluid water

            HX1 heat_exchanger power=10
            PA  pump head=6 flow=0.24
            PB  pump head=6 flow=0.24

            connections
            NA1 - PA - NA2 - HX1.in
            HX1.out - NA1
            NB1 - PB - NB2 - HX1.in2
            HX1.out2 - NB1

            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Code == "FS2216");
        Assert.Equal("300HE01", Exchanger1(result.Model).Tag);
    }

    [Fact]
    public void OneSideAgainstABoundaryIsOwnedByTheWiredSide()
    {
        // Rated mode: side 2 is a stated profile, not a circuit. Side 1's circuit owns it even though
        // heat leaves on side 2 -- there is no circuit there to own anything.
        var model = Model("""
            fluidscript 1
            circuit heating 100
            fluid water

            SP   pump
            SS   pipe length=30 dn=32
            SR   pipe length=30 dn=32
            LOAD heat_exchanger power=-150 dt=20
            HX1 heat_exchanger power=150 in=40 out=60 in2=85 out2=45

            connections
            HX1.out - SS - NSUP
            NSUP - LOAD - NRET
            NRET - SR - SP - HX1.in

            """);

        Assert.Equal("100HE02", Exchanger1(model).Tag);
    }

    [Fact]
    public void OwnershipMovesTheOrdinalsOfTheCircuitItJoins()
    {
        // The exchanger declared in the heating block but owned by the district: it counts among the
        // district's HE ordinals, not the heating circuit's, so LOAD is 100HE01 rather than 100HE02.
        var model = Model(Script(districtFirst: false, declaredInDistrict: false));

        Assert.Equal("100HE01", model.Components.Single(static c => c.Name == "LOAD").Tag);
        Assert.Equal("400HE01", Exchanger1(model).Tag);
    }
}
