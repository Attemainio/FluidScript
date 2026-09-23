using FluidScript.Core.Units;

namespace FluidScript.Core.Tests.Units;

public sealed class QuantityTests
{
    private static Quantity Written(double value, string unit, Dimension dimension)
    {
        Assert.True(UnitTable.TryResolve(unit, dimension, out var symbol));
        return Quantity.FromUnit(value, symbol);
    }

    private static Quantity Add(Quantity left, Quantity right)
    {
        Assert.True(Quantity.TryAdd(left, right, out var result, out var error), $"refused: {error}");
        return result;
    }

    private static Quantity Subtract(Quantity left, Quantity right)
    {
        Assert.True(Quantity.TrySubtract(left, right, out var result, out var error), $"refused: {error}");
        return result;
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(30, "kW")]
    [InlineData(30_000, "W")]
    public void Power_WrittenAnyWay_StoresWatts(double value, string unit)
    {
        Assert.Equal(30_000, Written(value, unit, Dimension.Power).SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABarePowerNumber_MeansKilowatts()
    {
        Assert.Equal(30_000, Quantity.FromBareNumber(30, Dimension.Power).SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABareLengthNumber_MeansMetres()
    {
        // The regression D-14 exists to prevent: one parameter meaning millimetres while its
        // neighbour means metres is exactly what made `length=25` ambiguous in the first place.
        Assert.Equal(45, Quantity.FromBareNumber(45, Dimension.Length).SiValue, 12);
        Assert.Equal(45, Written(45, "m", Dimension.Length).SiValue, 12);
        Assert.Equal(0.045, Written(45, "mm", Dimension.Length).SiValue, 12);
        Assert.Equal(4.5e-5, Written(0.045, "mm", Dimension.Length).SiValue, 15);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABareVolumeNumber_MeansLitres()
    {
        Assert.Equal(0.3, Quantity.FromBareNumber(300, Dimension.Volume).SiValue, 12);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ABareTemperature_MeansCelsius()
    {
        Assert.Equal(293.15, Quantity.FromBareNumber(20, Dimension.Temperature).SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ReadingPlusDifference_IsAReading()
    {
        // 20C + 30dK = 50C. The worked example the whole affine split exists for.
        var sum = Add(Written(20, "C", Dimension.Temperature), Written(30, "dK", Dimension.TemperatureDelta));

        Assert.Equal(Dimension.Temperature, sum.Dimension);
        Assert.Equal(323.15, sum.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ReadingPlusDifference_CommutesForAddition()
    {
        var sum = Add(Written(30, "dK", Dimension.TemperatureDelta), Written(20, "C", Dimension.Temperature));

        Assert.Equal(Dimension.Temperature, sum.Dimension);
        Assert.Equal(323.15, sum.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoReadings_CannotBeAdded()
    {
        // A single-dimension design returns 293.15 + 293.15 = 596.3 K here and draws a diagram.
        var refused = Quantity.TryAdd(
            Written(20, "C", Dimension.Temperature),
            Written(30, "C", Dimension.Temperature),
            out var result,
            out var error);

        Assert.False(refused);
        Assert.Equal(QuantityError.AbsoluteAddition, error);
        Assert.Equal(default, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TwoGaugePressures_CannotBeAddedEither()
    {
        Assert.False(Quantity.TryAdd(
            Written(300, "kPa", Dimension.Pressure),
            Written(100, "kPa", Dimension.Pressure),
            out _,
            out var error));
        Assert.Equal(QuantityError.AbsoluteAddition, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ReadingMinusDifference_IsAReading()
    {
        var result = Subtract(Written(70, "C", Dimension.Temperature), Written(20, "dK", Dimension.TemperatureDelta));

        Assert.Equal(Dimension.Temperature, result.Dimension);
        Assert.Equal(323.15, result.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ReadingMinusReading_IsADifference()
    {
        var result = Subtract(Written(70, "C", Dimension.Temperature), Written(20, "C", Dimension.Temperature));

        Assert.Equal(Dimension.TemperatureDelta, result.Dimension);
        Assert.Equal(50, result.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DifferenceMinusReading_IsRefused()
    {
        // Deliberately asymmetric: subtraction does not commute, and a type system that pretended it
        // did would accept this and return something.
        Assert.False(Quantity.TrySubtract(
            Written(20, "dK", Dimension.TemperatureDelta),
            Written(70, "C", Dimension.Temperature),
            out _,
            out var error));
        Assert.Equal(QuantityError.AbsoluteSubtractedFromDifference, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MismatchedDimensions_AreRefused()
    {
        Assert.False(Quantity.TryAdd(
            Quantity.FromBareNumber(30, Dimension.Power),
            Quantity.FromBareNumber(2, Dimension.Length),
            out _,
            out var error));
        Assert.Equal(QuantityError.DimensionMismatch, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void AHeadAndALength_CannotBeAdded()
    {
        Assert.False(Quantity.TryAdd(
            Quantity.FromSi(15, Dimension.Head),
            Quantity.FromBareNumber(2, Dimension.Length),
            out _,
            out var error));
        Assert.Equal(QuantityError.DimensionMismatch, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DividingByADifference_CarriesAnUnnamedDimension()
    {
        // 30 kW / 10 dK computes fine and fails only where it is stored.
        Assert.True(Quantity.TryDivide(
            Quantity.FromBareNumber(30, Dimension.Power),
            Quantity.FromBareNumber(10, Dimension.TemperatureDelta),
            out var result,
            out _));

        Assert.False(result.Dimension.IsNamed);
        Assert.Equal("W/K", result.Dimension.Name);
        Assert.Equal(3000, result.SiValue, 6);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void PowerOverSpecificHeatTimesDifference_IsAMassFlow()
    {
        // The cooling loop's own arithmetic: 30 kW at 4.184 kJ/(kg K) over 30 K.
        var capacity = Written(4.184, "kJ/(kg*K)", Dimension.SpecificHeat);
        var rise = Written(30, "dK", Dimension.TemperatureDelta);

        Assert.True(Quantity.TryMultiply(capacity, rise, out var perKilogram, out _));
        Assert.Equal(Dimension.Enthalpy, perKilogram.Dimension);

        Assert.True(Quantity.TryDivide(
            Quantity.FromBareNumber(30, Dimension.Power), perKilogram, out var flow, out _));

        Assert.Equal(Dimension.MassFlow, flow.Dimension);
        Assert.Equal(0.2390, flow.SiValue, 4);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScalingByAPlainNumber_KeepsTheDimensionExactly()
    {
        // 2 x 30 dK must stay a temperature difference rather than resolving to an anonymous kelvin.
        Assert.True(Quantity.TryMultiply(
            Written(30, "dK", Dimension.TemperatureDelta),
            Quantity.FromSi(2, Dimension.Dimensionless),
            out var scaled,
            out _));

        Assert.Equal(Dimension.TemperatureDelta, scaled.Dimension);
        Assert.Equal(60, scaled.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ScalingAReading_IsRefused()
    {
        // Doubling 20 C has no meaning: the answer depends on where the scale's zero was put.
        Assert.False(Quantity.TryMultiply(
            Written(20, "C", Dimension.Temperature),
            Quantity.FromSi(2, Dimension.Dimensionless),
            out _,
            out var error));
        Assert.Equal(QuantityError.AffineOperand, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NegatingAReading_IsRefused()
    {
        Assert.False(Quantity.TryNegate(Written(20, "C", Dimension.Temperature), out _, out var error));
        Assert.Equal(QuantityError.AffineOperand, error);

        Assert.True(Quantity.TryNegate(Written(20, "dK", Dimension.TemperatureDelta), out var negated, out _));
        Assert.Equal(-20, negated.SiValue, 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ADesignation_TakesNoPartInArithmetic()
    {
        // Twice DN50 is not DN100.
        Assert.False(Quantity.TryMultiply(
            Quantity.FromSi(50, Dimension.NominalDiameter),
            Quantity.FromSi(2, Dimension.Dimensionless),
            out _,
            out var error));
        Assert.Equal(QuantityError.NominalOperand, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DividingByZero_IsRefusedRatherThanInfinite()
    {
        Assert.False(Quantity.TryDivide(
            Quantity.FromBareNumber(30, Dimension.Power),
            Quantity.FromSi(0, Dimension.Dimensionless),
            out _,
            out var error));
        Assert.Equal(QuantityError.DivisionByZero, error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Equality_IgnoresTheUnitTheValueWasWrittenIn()
    {
        var asKilowatts = Written(30, "kW", Dimension.Power);
        var asWatts = Written(30_000, "W", Dimension.Power);

        Assert.Equal(asKilowatts, asWatts);
        Assert.Equal(asKilowatts.GetHashCode(), asWatts.GetHashCode());
        Assert.False(asKilowatts.EqualsExactly(asWatts));
        Assert.True(asKilowatts.EqualsExactly(Written(30, "kW", Dimension.Power)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Equality_SeparatesDimensionsThatShareAValue()
    {
        Assert.NotEqual(Quantity.FromSi(15, Dimension.Head), Quantity.FromSi(15, Dimension.Length));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsCloseTo_HasAFloorSoNearZeroComparisonsSurvive()
    {
        var sum = Quantity.FromSi(0.1 + 0.2, Dimension.Dimensionless);
        var expected = Quantity.FromSi(0.3, Dimension.Dimensionless);

        Assert.NotEqual(expected, sum);
        Assert.True(sum.IsCloseTo(expected));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsCloseTo_RefusesADimensionMismatch()
    {
        Assert.False(Quantity.FromSi(1, Dimension.Length).IsCloseTo(Quantity.FromSi(1, Dimension.Head)));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValueIn_ConvertsBackForDisplay()
    {
        var power = Quantity.FromBareNumber(30, Dimension.Power);

        Assert.True(UnitTable.TryResolve("kW", Dimension.Power, out var kilowatts));
        Assert.Equal(30, power.ValueIn(kilowatts), 9);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ValueIn_RefusesAUnitFromAnotherDimension()
    {
        var power = Quantity.FromBareNumber(30, Dimension.Power);
        Assert.True(UnitTable.TryResolve("m", Dimension.Length, out var metres));

        Assert.Throws<ArgumentException>(() => power.ValueIn(metres));
    }
}
