using FluidScript.Core.Physics.Fluids;
using FluidScript.Core.Physics.Fluids.Substances;
using FluidScript.Core.Physics.Units;

namespace FluidScript.Core.Tests.Physics.Fluids;

/// <summary>A property read must not keep native memory: the guard that would have caught <c>C-76</c>.</summary>
/// <remarks>
/// <para>
/// <strong>The managed heap cannot see this defect, so no allocation test can.</strong> The backend's
/// native CoolProp state is ~540 KB and the managed object holding it is a few dozen bytes; a read that
/// clones the state and drops it leaks half a megabyte the GC never feels, and 20 000 such reads
/// measured +10.8 GB of working set (`C-76`). Two thousand reads here would leak about a gigabyte, so
/// the 64 MB bound leaves room for the GC's own heap growth and none for the defect.
/// </para>
/// <para>
/// <strong>Working set, not GC statistics, and no forced collection.</strong> A solve does not collect
/// between residual sweeps either, and the leak is exactly what a normal run keeps; measuring after a
/// `GC.Collect` would pass a slow leak the finalizers happen to recover.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
[Collection(SoleOccupancy.Name)]
public sealed class NativeMemoryTests
{
    private const int Reads = 2_000;
    private const double AllowedGrowthMiB = 64;

    public static TheoryData<string> Substances => new() { "water", "ammonia", "humid air" };

    [Theory]
    [MemberData(nameof(Substances))]
    public void TwoThousandStateReadsKeepNoNativeMemory(string substance)
    {
        var read = substance switch
        {
            "water" => Read(Water.Instance, 100_000, 290, 350),
            "ammonia" => Read(Refrigerant.Ammonia, 400_000, 260, 300),
            _ => ReadAir(),
        };

        // One cold read pays for the native fluid itself and every lazily loaded table; it is not a leak.
        read(0);

        var before = Native();

        for (var index = 1; index <= Reads; index++)
        {
            read(index);
        }

        var grewBy = (Native() - before) / (1024.0 * 1024.0);

        Assert.True(
            grewBy < AllowedGrowthMiB,
            $"{Reads} {substance} reads grew the working set by {grewBy:F0} MiB; a read is keeping native memory.");
    }

    /// <summary>What the process holds outside the managed heap: working set less what the GC has committed.</summary>
    /// <remarks>
    /// The working set alone would count the managed heap's growth, which the GC returns and which
    /// belongs to whatever else the process is doing; the difference is native memory and the runtime.
    /// </remarks>
    private static long Native() => Environment.WorkingSet - GC.GetGCMemoryInfo().TotalCommittedBytes;

    private static Action<int> Read(ISubstance substance, double gaugePressure, double lowK, double highK) =>
        index =>
        {
            var kelvin = lowK + (highK - lowK) * (index % 97) / 97.0;
            var state = substance.FromPressureTemperature(
                Quantity.FromSi(gaugePressure, Dimension.Pressure), Quantity.FromSi(kelvin, Dimension.Temperature));

            Assert.True(state.IsSuccess, state.Error?.Message);
            Assert.True(state.Value.Density.SiValue > 0);
        };

    private static Action<int> ReadAir() =>
        index =>
        {
            var kelvin = 285 + 20.0 * (index % 97) / 97.0;
            var state = HumidAirSubstance.Instance.FromPressureTemperatureRelativeHumidity(
                Quantity.FromSi(0, Dimension.Pressure),
                Quantity.FromSi(kelvin, Dimension.Temperature),
                Quantity.FromSi(0.5, Dimension.Dimensionless));

            Assert.True(state.IsSuccess, state.Error?.Message);
        };
}
