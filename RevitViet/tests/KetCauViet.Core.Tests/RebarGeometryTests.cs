using KetCauViet.Core;
using Xunit;

namespace KetCauViet.Core.Tests;

// Analytic geometry fixtures. These tests do not load or execute Autodesk Revit.
public sealed class RebarGeometryTests
{
    private static Vector3 P(double x, double y, double z = 0) => new(x, y, z);
    private static Vector3[] Rectangle(double w = 300, double h = 600) => new[] { P(0, 0), P(w, 0), P(w, h), P(0, h) };

    [Theory]
    [InlineData(300, 600)]
    [InlineData(400, 400)]
    public void BeamAndColumnAreRecognizedFromSectionContours(double w, double h)
    {
        var section = RectangleSection.Recognize(Rectangle(w, h), 1, .01);
        Assert.Equal(w, section.MaxU - section.MinU, 8); Assert.Equal(h, section.MaxV - section.MinV, 8);
    }

    [Fact]
    public void CollinearFaceSplitsDoNotTurnARectangleIntoUnsupportedProfile()
    {
        var section = RectangleSection.Recognize(new[] { P(0, 0), P(150, 0), P(300, 0), P(300, 600), P(0, 600), P(0, 300) }, 1, .01);
        Assert.Equal(300, section.MaxU);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.7, 0)]
    [InlineData(1.2, 0.3)]
    [InlineData(-2, -0.6)]
    public void RotatedAndSlopingMembersKeepSectionDimensions(double yaw, double pitch)
    {
        var axis = P(Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(yaw) * Math.Cos(pitch), Math.Sin(pitch));
        var frame = new MemberFrame(P(100000, -250000, 20000), axis, P(0, 0, 1));
        var outline = Rectangle().Select(p => frame.ToWorld(p + P(0, 0, 3000))).Select(frame.ToLocal).ToList();
        var section = RectangleSection.Recognize(outline, 1, .01);
        Assert.Equal(300, section.MaxU - section.MinU, 6); Assert.Equal(600, section.MaxV - section.MinV, 6);
        Assert.InRange(frame.U.Cross(frame.V).Dot(frame.Axis), .9999999, 1.0000001);
    }

    [Fact]
    public void RotatedColumnHasRightHandedFrameIndependentOfFamilyAxisLabels()
    {
        var frame = new MemberFrame(P(0, 0, 0), P(0, 0, 1), P(-.6, .8, 0));
        var p = P(150, -200, 3000);
        Assert.InRange((frame.ToLocal(frame.ToWorld(p)) - p).Length, 0, 1e-8);
    }

    [Fact]
    public void JoinedAndCutVolumeDoesNotDetermineNominalSection()
    {
        var nominal = RectangleSection.Recognize(Rectangle(), 1, .01);
        // A 45-degree cut at the end removes volume, but leaves the same rectangular
        // cross section in the body. The clearance plane trims the bar instead of rejecting the host.
        var cut = RebarPlanning.ClipConvex(P(150, 300, 0), P(0, 0, 1), new(0, 6000),
            new[] { (P(0, 0, -1), 0d), (P(1, 0, 1), 6000d) }, 35);
        Assert.NotNull(cut); Assert.Equal(300, nominal.MaxU);
        Assert.Equal(6000 - 150 - 35 * Math.Sqrt(2), cut!.Value.End, 7);
        Assert.Equal(35, cut.Value.Start, 8);
    }

    [Fact]
    public void AdjacentJoinedConcreteIntervalsAreMergedWithoutAnArtificialGap()
    {
        var merged = RebarPlanning.MergeIntervals(new[] { new LinearInterval(0, 5000), new LinearInterval(5000, 6000), new LinearInterval(5900, 6200) }, .01);
        Assert.Single(merged); Assert.Equal(6200, merged[0].End);
    }

    [Fact]
    public void RealOpeningRemainsAGapAfterIntervalMerge()
    {
        var merged = RebarPlanning.MergeIntervals(new[] { new LinearInterval(0, 2000), new LinearInterval(2200, 6000) }, .01);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void LSectionIsRejectedEvenThoughItsBoundingBoxIsRectangular() => Assert.Throws<ArgumentException>(() => RectangleSection.Recognize(new[] { P(0, 0), P(300, 0), P(300, 200), P(100, 200), P(100, 600), P(0, 600) }, 1, .01));

    [Fact]
    public void CircularProfileIsRejectedWithSpecificProfileFailure() => Assert.Throws<ArgumentException>(() => RectangleSection.Recognize(Enumerable.Range(0, 16).Select(i => P(100 * Math.Cos(i * Math.PI / 8), 100 * Math.Sin(i * Math.PI / 8))), 1, .01));

    [Fact]
    public void HollowProfileRequiresSpecialLayout() => Assert.Throws<ArgumentException>(() => RectangleSection.Recognize(Rectangle(), 2, .01));

    [Fact]
    public void TaperedNominalSectionIsNotMistakenForAnEndCut()
    {
        var a = RectangleSection.Recognize(Rectangle(), 1, .01);
        var b = RectangleSection.Recognize(Rectangle(200, 600), 1, .01);
        Assert.Throws<ArgumentException>(() => a.RequireSame(b, .01));
    }

    [Fact]
    public void CoverThatConsumesTheSectionProducesNoValidInterval()
    {
        Assert.Null(RebarPlanning.ClipConvex(P(0, 0, 0), P(1, 0, 0), new(0, 300), new[] { (P(1, 0, 0), 300d), (P(-1, 0, 0), 0d) }, 160));
    }

    [Fact]
    public void ThreeStirrupZonesHaveNoDuplicatedBoundaryBarsAndRespectEachMaximumSpacing()
    {
        var values = RebarPlanning.Zones(0, 6000, 1000, 1000, 100, 200, 125);
        Assert.Equal(values.Count, values.Distinct().Count()); Assert.Equal(0, values.First()); Assert.Equal(6000, values.Last());
        foreach (var pair in values.Zip(values.Skip(1), (a, b) => (a, b)))
            Assert.True(pair.b - pair.a <= (pair.b <= 1000 ? 100 : pair.a >= 5000 ? 125 : 200) + 1e-8);
    }

    [Fact]
    public void OverlappingEndZonesAreRejected() => Assert.Throws<ArgumentException>(() => RebarPlanning.Zones(0, 1000, 600, 600, 100, 200, 100));

    [Fact]
    public void SplicedSegmentsHaveTheRequestedPhysicalOverlap()
    {
        var segments = RebarPlanning.Splice(0, 18000, 6000, 600);
        Assert.Equal(4, segments.Count); Assert.Equal(18000, segments.Last().End);
        Assert.All(segments, s => Assert.InRange(s.Length, 1, 6000));
        foreach (var pair in segments.Zip(segments.Skip(1), (a, b) => (a, b))) Assert.Equal(600, pair.a.End - pair.b.Start, 8);
    }

    [Fact]
    public void SpliceCannotExceedStockLength() => Assert.Throws<ArgumentException>(() => RebarPlanning.Splice(0, 18000, 6000, 6000));

    [Fact]
    public void TangentArcAtExactCoverIsNotRejectedByAnInflatedChordEnd()
    {
        var max=RebarClearance.ArcMaximumDot(P(20,20),P(1,0),P(0,1),20,Math.PI,1.5*Math.PI,P(-1,0));
        Assert.Equal(0,max,9);
        var inset=RebarClearance.LongitudinalInset(4,8,20);
        var radialDistance=20-Math.Sqrt(2)*(20-inset);
        Assert.Equal(12,radialDistance,8);
    }

    [Fact]
    public void ArcExtremaIncludesInteriorAndWrappedAngles()
    {
        Assert.Equal(10,RebarClearance.ArcMaximumDot(P(0,0),P(1,0),P(0,1),10,-.5,.5,P(1,0)),9);
        Assert.Equal(10,RebarClearance.ArcMaximumDot(P(0,0),P(1,0),P(0,1),10,5.5,7,P(1,0)),9);
    }

    [Fact]
    public void ColumnHookSeamIsBetweenBarsInsteadOfOnTheMiddleLongitudinalBar()
    {
        var positions=Engineering.EvenPositions(43,357,3);
        var seam=RebarClearance.HookSeam(positions);
        Assert.Equal(121.5,seam,8);Assert.All(positions,p=>Assert.True(Math.Abs(p-seam)>70));
    }

    [Fact]
    public void AuxiliaryTiesCrossInDifferentAxialPlanes()
    {
        var u=new BarSegment(P(212,29,10),P(212,371,10),4,"U");
        var v=new BarSegment(P(29,212,20),P(371,212,20),4,"V");
        Assert.Null(RebarClashes.FindCollision(new[]{u,v},.2,200));
        Assert.NotNull(RebarClashes.FindCollision(new[]{u,new BarSegment(P(29,212,10),P(371,212,10),4,"V")},.2,200));
    }

    [Fact]
    public void CrossedBarsAreARealClashAndTouchingLayersAreAllowed()
    {
        var a=new BarSegment(P(0,0),P(1000,0),5,"A");
        Assert.NotNull(RebarClashes.FindCollision(new[]{a,new BarSegment(P(500,-500),P(500,500),5,"B")},.1,100));
        Assert.Null(RebarClashes.FindCollision(new[]{a,new BarSegment(P(500,-500,10),P(500,500,10),5,"B")},.1,100));
    }

    [Fact]
    public void SpliceLanesAvoidCoincidentSteel()
    {
        var a=new BarSegment(P(0,0),P(6000,0),8,"A");
        var b=new BarSegment(P(5400,41),P(11000,41),8,"B");
        Assert.Null(RebarClashes.FindCollision(new[]{a,b},.1,200));
        Assert.NotNull(RebarClashes.FindCollision(new[]{a,new BarSegment(P(5400,0),P(11000,0),8,"B")},.1,200));
    }
}
