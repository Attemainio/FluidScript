using System.Globalization;
using System.Text;

namespace FluidScript.Fixtures;

/// <summary>Generated scripts at the scale the quality attributes are stated for.</summary>
/// <remarks>
/// <para>
/// <c>07</c> states the payload and render budgets for a <em>200-component reference model</em> and
/// <c>62</c> uses the same fixture for non-overlap. No hand-written sample is that size, and a
/// checked-in file of that size would be solved by every corpus test, so the model is generated: the
/// distribution header of <c>01</c> with as many pumped consumers as asked for, each the shape of
/// <c>samples/m2-distribution-header.fluid</c>'s AHU branch. The count is what the contract lists --
/// declared components, header nodes, and the nodes <c>I2</c> adds between adjacent non-node
/// components -- so eighteen consumers make exactly two hundred.
/// </para>
/// </remarks>
public static class ReferenceModels
{
    /// <summary>The consumer count at which <see cref="DistributionHeader"/> lists 200 components.</summary>
    public const int TwoHundredComponentConsumers = 18;

    /// <summary>Components per consumer branch as the contract counts them.</summary>
    /// <remarks>Load, valve, pump, two pipes, the mixing node, a supply-header and a return-header node, and three inferred nodes.</remarks>
    public const int ComponentsPerConsumer = 11;

    /// <summary>Components outside the branches: the source exchanger and the datum node.</summary>
    public const int HeaderComponents = 2;

    /// <summary>A distribution header with <paramref name="consumers"/> pumped, valve-blended consumers.</summary>
    /// <param name="consumers">How many branches; each adds <see cref="ComponentsPerConsumer"/> components.</param>
    /// <returns>A complete, solvable script.</returns>
    public static string DistributionHeader(int consumers)
    {
        var script = new StringBuilder();
        var power = 0.0;

        for (var k = 1; k <= consumers; k++)
        {
            power += LoadKilowatts(k);
        }

        script.Append("fluidscript 1\n");
        script.Append("project static header_").Append(consumers.ToString(CultureInfo.InvariantCulture)).Append('\n');
        script.Append('\n');
        script.Append("circuit heating 100\n");
        script.Append("fluid water\n");
        script.Append('\n');
        script.Append("HS1     heat_exchanger power=").Append(power.ToString("0.#", CultureInfo.InvariantCulture)).Append(" kW out.t=60\n");
        script.Append('\n');
        script.Append("connections\n");
        script.Append("N1 - HS1 - S1\n");

        for (var k = 1; k < consumers; k++)
        {
            script.Append('S').Append(k).Append(" - S").Append(k + 1).Append('\n');
        }

        for (var k = consumers; k > 1; k--)
        {
            script.Append('R').Append(k).Append(" - R").Append(k - 1).Append('\n');
        }

        script.Append("R1 - N1\n");
        script.Append('\n');
        script.Append("N1 node p=250\n");

        for (var k = 1; k <= consumers; k++)
        {
            var n = k.ToString(CultureInfo.InvariantCulture);
            var length = (10 + (2 * (k % 5))).ToString(CultureInfo.InvariantCulture);

            script.Append('\n');
            script.Append("circuit consumer_").Append(n).Append(' ').Append(100 + k).Append('\n');
            script.Append('\n');
            script.Append("HE_").Append(n).Append("  load in.t=50 out.t=30 power=").Append(LoadKilowatts(k).ToString("0.#", CultureInfo.InvariantCulture)).Append(" kW\n");
            script.Append("TV_").Append(n).Append("  three_way_valve\n");
            script.Append("PU_").Append(n).Append("  pump\n");
            script.Append("PA_").Append(n).Append("  pipe length=").Append(length).Append(" dn=25\n");
            script.Append("PB_").Append(n).Append("  pipe length=").Append(length).Append(" dn=25\n");
            script.Append('\n');
            script.Append("connections\n");
            script.Append('S').Append(n).Append(" - PA_").Append(n).Append(" - TV_").Append(n).Append(".a\n");
            script.Append("NM_").Append(n).Append(" - TV_").Append(n).Append(".b\n");
            script.Append("TV_").Append(n).Append(".ab - PU_").Append(n).Append(" - HE_").Append(n).Append(" - NM_").Append(n).Append('\n');
            script.Append("NM_").Append(n).Append(" - PB_").Append(n).Append(" - R").Append(n).Append('\n');
        }

        return script.ToString();
    }

    private static double LoadKilowatts(int k) => 20 + (2 * (k % 7));
}
