using KetCauViet.Core;
using Xunit;

namespace KetCauViet.Core.Tests;

// Tests the production lifetime/selection policy. Native Revit execution is separate.
public sealed class TypeSelectionTests
{
    private sealed class DocumentWrapper
    {
        public Guid Native { get; }
        public string Title { get; } = "Nhà 10 tầng";
        public DocumentWrapper(Guid native) => Native = native;
        public override bool Equals(object? obj) => obj is DocumentWrapper d && d.Native == Native;
        public override int GetHashCode() => Native.GetHashCode();
    }
    private sealed class TypeWrapper
    {
        public DocumentWrapper Owner { get; }
        public bool IsValid { get; set; } = true;
        public TypeWrapper(DocumentWrapper owner) => Owner = owner;
    }
    private static TypeStateData Data(int id = 100, string uid = "bar-100", string revision = "v1", string name = "D16", double diameter = 16, double bend = 64)
        => new(id, uid, revision, name, diameter, diameter, bend, bend);

    [Fact]
    public void SameNativeDocumentWithDifferentManagedWrappersMustBeAccepted()
    {
        var native = Guid.NewGuid(); var ui = new DocumentWrapper(native); var owner = new DocumentWrapper(native);
        Assert.NotSame(ui, owner); Assert.True(ui != owner); Assert.True(ui.Equals(owner));
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, id => new(new TypeWrapper(owner), Data(), ownerMatches: owner.Equals(ui)));
        Assert.NotNull(scope.Resolve(scope.Capture(100)));
    }

    [Fact]
    public void CompleteProjectCanChooseThreeSeparateRoles()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, id => new(new TypeWrapper(owner), Data(id, "type-" + id, name: "Type " + id, diameter: id == 200 ? 8 : id == 300 ? 12 : 16)));
        var main = scope.Capture(100); var tie = scope.Capture(200); var distribution = scope.Capture(300);
        Assert.Empty(scope.Preflight(new[] { ("Dọc", main, false), ("Đai", tie, true), ("Phân bố", distribution, false) }));
        Assert.Equal(16, main.NominalMm); Assert.Equal(8, tie.NominalMm); Assert.Equal(12, distribution.NominalMm);
    }

    [Fact]
    public void NoBarTypesProducesAConfigurationFailureBeforeAnyHost()
    {
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => null);
        var ex = Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100));
        Assert.Equal("TYPE_DELETED", ex.Code);
    }

    [Fact]
    public void ChangingTheActiveProjectInvalidatesTheCurrentInvocation()
    {
        var a = new DocumentWrapper(Guid.NewGuid()); var b = new DocumentWrapper(Guid.NewGuid()); var active = a;
        using var scope = new TypeSelectionScope<TypeWrapper>(() => a.Equals(active), _ => new(new TypeWrapper(a), Data()));
        var choice = scope.Capture(100); active = b;
        Assert.Equal("DOCUMENT_CHANGED", Assert.Throws<TypeSelectionFailure>(() => scope.Resolve(choice)).Code);
    }

    [Fact]
    public void SameNamesIdsUniqueIdsAndVersionsInAnotherProjectDoNotMakeAChoicePortable()
    {
        var a = new DocumentWrapper(Guid.NewGuid()); var b = new DocumentWrapper(Guid.NewGuid());
        Assert.Equal(a.Title, b.Title);
        using var first = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(a), Data()));
        using var second = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(b), Data()));
        var choice = first.Capture(100);
        Assert.Equal("FOREIGN_SCOPE", Assert.Throws<TypeSelectionFailure>(() => second.Resolve(choice)).Code);
        Assert.NotNull(second.Resolve(second.Capture(100)));
    }

    [Fact]
    public void ReaderReturningAnElementFromAnotherDocumentIsRejected()
    {
        var a = new DocumentWrapper(Guid.NewGuid()); var b = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(b), Data(), ownerMatches: a.Equals(b)));
        Assert.Equal("FOREIGN_DOCUMENT", Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100)).Code);
    }

    [Fact]
    public void ADeletedTypeAfterOpeningTheDialogRequiresReselection()
    {
        var exists = true; var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => exists ? new(new TypeWrapper(owner), Data()) : null);
        var choice = scope.Capture(100); exists = false;
        Assert.Equal("TYPE_DELETED", Assert.Throws<TypeSelectionFailure>(() => scope.Resolve(choice)).Code);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("diameter")]
    [InlineData("name")]
    [InlineData("bend")]
    public void ChangedTypeIsDetectedEvenWhenItsIdAndUniqueIdStayTheSame(string change)
    {
        var data = Data(); var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), data));
        var choice = scope.Capture(100);
        data = Data(revision: change == "revision" ? "v2" : "v1", name: change == "name" ? "Renamed" : "D16", diameter: change == "diameter" ? 20 : 16, bend: change == "bend" ? 80 : 64);
        Assert.Equal("TYPE_CHANGED", Assert.Throws<TypeSelectionFailure>(() => scope.Resolve(choice)).Code);
    }

    [Fact]
    public void ReusedNumericIdCannotSilentlySelectADifferentElement()
    {
        var uid = "first"; var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data(uid: uid)));
        var choice = scope.Capture(100); uid = "replacement";
        Assert.Equal("REPLACED_ID", Assert.Throws<TypeSelectionFailure>(() => scope.Resolve(choice)).Code);
    }

    [Fact]
    public void AnElementOfTheWrongApiClassCannotBeUsedAsABarType()
    {
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(null, Data(), barType: false));
        Assert.Equal("WRONG_CLASS", Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100)).Code);
    }

    [Fact]
    public void InvalidObjectIsRejectedBeforeAnyIdentityOrGeometryAssumption()
    {
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(null, Data(), valid: false));
        Assert.Equal("INVALID_OBJECT", Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100)).Code);
    }

    [Fact]
    public void BatchOf466HostsRebindsWrappersInsteadOfKeepingTheFirstWrapper()
    {
        var owner = new DocumentWrapper(Guid.NewGuid()); var reads = 0;
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => { reads++; return new(new TypeWrapper(owner), Data()); });
        var choice = scope.Capture(100); TypeWrapper? previous = null;
        for (var host = 0; host < 466; host++)
        {
            var current = scope.Resolve(choice); Assert.True(current.IsValid); Assert.NotSame(previous, current);
            current.IsValid = false; // Models an obsolete managed wrapper after rollback.
            previous = current;
        }
        Assert.Equal(467, reads);
    }

    [Fact]
    public void SharedConfigurationFailureDoesNotInvoke466HostOperations()
    {
        var exists = true; var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => exists ? new(new TypeWrapper(owner), Data()) : null);
        var choice = scope.Capture(100); exists = false; var operations = 0;
        var errors = scope.Preflight(new[] { ("Dọc", choice, false), ("Đai", choice, true) });
        if (errors.Count == 0) foreach (var host in Enumerable.Range(0, 466)) operations++;
        Assert.Equal(0, operations); Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void RepeatedCommandsDoNotReuseThePreviousCommandsChoices()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        var first = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data()));
        var old = first.Capture(100); first.Dispose();
        Assert.Equal("SCOPE_CLOSED", Assert.Throws<TypeSelectionFailure>(() => first.Resolve(old)).Code);
        using var second = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data()));
        Assert.Equal("FOREIGN_SCOPE", Assert.Throws<TypeSelectionFailure>(() => second.Resolve(old)).Code);
        Assert.NotNull(second.Resolve(second.Capture(100)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-16)]
    [InlineData(double.NaN)]
    public void InvalidDiameterIsFilteredFromChoices(double diameter)
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data(diameter: diameter)));
        Assert.Equal("INVALID_DIAMETER", Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100)).Code);
    }

    [Fact]
    public void BendValidationRemainsEnabledForTheChosenRole()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data(bend: 8)));
        var choice = scope.Capture(100);
        Assert.Equal("INVALID_BEND", Assert.Throws<TypeSelectionFailure>(() => scope.Resolve(choice, true)).Code);
    }

    [Fact]
    public void NominalAndModelDiametersAreKeptSeparate()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        var data = new TypeStateData(100, "bar-100", "v1", "D16", 16, 18, 72, 72);
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), data));
        var choice = scope.Capture(100);
        Assert.Equal(16, choice.NominalMm); Assert.Equal(18, choice.ModelMm);
        Assert.NotNull(scope.Resolve(choice));
        data = new TypeStateData(100, "bar-100", "v1", "D16", 16, 20, 72, 72);
        Assert.Equal("TYPE_CHANGED", Assert.Throws<TypeSelectionFailure>(() => scope.Resolve(choice)).Code);
    }

    [Fact]
    public void AValidNominalDiameterCannotHideAnInvalidModelDiameter()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), new TypeStateData(100, "bar-100", "v1", "D16", 16, 0, 64, 64)));
        Assert.Equal("INVALID_DIAMETER", Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100)).Code);
    }

    [Fact]
    public void PreflightReportsTheInvalidRoleAndRetainsValidSelections()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), new TypeStateData(100, "bar-100", "v1", "D16", 16, 16, 64, 8)));
        var choice = scope.Capture(100);
        var errors = scope.Preflight(new[] { ("Dọc", choice, false), ("Đai", choice, true), ("Phân bố", choice, false) });
        var error = Assert.Single(errors); Assert.StartsWith("Đai [INVALID_BEND]", error);
        Assert.NotNull(scope.Resolve(choice));
    }

    [Fact]
    public void AReaderCannotReturnAnUnrelatedId()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data(id: 200)));
        Assert.Equal("INVALID_ID", Assert.Throws<TypeSelectionFailure>(() => scope.Capture(100)).Code);
    }

    [Fact]
    public void TwoValidTypesWithTheSameNameKeepTheirExplicitIdentity()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, id => new(new TypeWrapper(owner), Data(id, "uid-" + id, name: "D16")));
        var first = scope.Capture(100); var second = scope.Capture(200);
        Assert.Equal(first.Name, second.Name); Assert.NotEqual(first.UniqueId, second.UniqueId);
        Assert.NotEqual(first.ElementId, second.ElementId); Assert.NotNull(scope.Resolve(first)); Assert.NotNull(scope.Resolve(second));
    }

    [Fact]
    public void DiameterMatchingOnlyAllowsUnitConversionRoundoff()
    {
        var owner = new DocumentWrapper(Guid.NewGuid());
        using var scope = new TypeSelectionScope<TypeWrapper>(() => true, _ => new(new TypeWrapper(owner), Data()));
        var choice = scope.Capture(100);
        Assert.True(choice.MatchesNominal(16)); Assert.True(choice.MatchesNominal(16.000000000000004));
        Assert.False(choice.MatchesNominal(16.04)); Assert.False(choice.MatchesNominal(20)); Assert.False(choice.MatchesNominal(double.NaN));
    }
}
