using KetCauViet.Core;
using Xunit;

namespace KetCauViet.Core.Tests;

public sealed class EngineeringTests
{
    [Fact]
    public void RevitFeetConvertToMetricWithTheCorrectPower()
    {
        Assert.Equal(1, Engineering.Feet(304.8), 10);
        Assert.Equal(0.3048, Engineering.Metres(1), 10);
        Assert.Equal(0.09290304, Engineering.SquareMetres(1), 10);
        Assert.Equal(0.028316846592, Engineering.CubicMetres(1), 12);
    }

    [Theory]
    [InlineData("25,5", 25.5)]
    [InlineData("25.5", 25.5)]
    [InlineData(" -12,25 ", -12.25)]
    public void VietnameseAndInvariantDecimalInputsAgree(string input, double expected) => Assert.Equal(expected, Engineering.ParseNumber(input));

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1.234,56")]
    [InlineData("")]
    public void InvalidOrAmbiguousNumbersAreRejected(string input) => Assert.Throws<ArgumentException>(() => Engineering.ParseNumber(input));

    [Fact]
    public void TenMillimetreBarWeighsAboutPointSixOneSevenKilogramsPerMetre() => Assert.InRange(Engineering.RebarMass(1, 10, 7850), 0.6165, 0.6166);

    [Fact]
    public void RebarMassUsesTotalLengthWithoutMultiplyingQuantityTwice() => Assert.Equal(10 * Engineering.RebarMass(1, 16, 7850), Engineering.RebarMass(10, 16, 7850), 10);

    [Theory]
    [InlineData(-1, 10, 7850)]
    [InlineData(1, 0, 7850)]
    [InlineData(1, 10, 0)]
    public void InvalidMassInputsFail(double length, double diameter, double density) => Assert.Throws<ArgumentOutOfRangeException>(() => Engineering.RebarMass(length, diameter, density));

    [Fact]
    public void WaterproofingAccountsForLayersAndOverlap() => Assert.Equal(210, Engineering.WaterproofArea(100, 2, 5), 10);

    [Fact]
    public void InvalidWaterproofLayersFail() => Assert.Throws<ArgumentOutOfRangeException>(() => Engineering.WaterproofArea(100, 0, 5));

    [Fact]
    public void PerimeterPositionsIncludeBothEdgesWithoutRoundingDrift()
    {
        var positions = Engineering.EvenPositions(-0.3, 0.3, 4);
        Assert.Equal(4, positions.Count); Assert.Equal(-0.3, positions.First(), 10); Assert.Equal(0.3, positions.Last(), 10);
        Assert.All(positions.Zip(positions.Skip(1), (a, b) => b - a), gap => Assert.Equal(0.2, gap, 10));
    }

    [Theory]
    [InlineData(1000, 300, 5)]
    [InlineData(900, 300, 4)]
    [InlineData(100, 300, 2)]
    public void MaximumSpacingRoundsUpTheNumberOfIntervals(double length, double spacing, int expected) => Assert.Equal(expected, Engineering.SpacingCount(length, spacing));

    [Fact]
    public void DuplicateMarksAreAvoidedCaseInsensitively()
    {
        var sequence = new NumberSequence("D-", 1, 3, new[] { "d-001", "D-003", "OTHER" });
        Assert.Equal("D-002", sequence.Next()); Assert.Equal("D-004", sequence.Next());
    }

    [Fact]
    public void CsvRetainsVietnameseAndQuotesDelimitersAndNewlines()
    {
        var csv = Engineering.Csv(new[] { new[] { "Cột;\"A\"", "Tầng\n1" } });
        Assert.Contains("\"Cột;\"\"A\"\"\";\"Tầng\n1\"", csv);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"example\")")]
    [InlineData(" +SUM(A1:A3)")]
    [InlineData("@SUM(A1)")]
    [InlineData("-CMD()")]
    public void CsvNeutralizesSpreadsheetFormulaInjection(string unsafeValue) => Assert.StartsWith("\"'", Engineering.Csv(new[] { new[] { unsafeValue } }));

    [Fact]
    public void CsvDoesNotTurnNegativeCoordinatesIntoText() => Assert.StartsWith("\"-123,45\"", Engineering.Csv(new[] { new[] { "-123,45" } }));
}
