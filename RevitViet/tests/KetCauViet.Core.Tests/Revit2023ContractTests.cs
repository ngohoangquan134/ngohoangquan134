using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Xunit;

namespace KetCauViet.Core.Tests;

// Read the real Autodesk reference metadata and API documentation. Do not load the
// reference assembly as a runtime or claim that these are Revit integration tests.
public sealed class Revit2023ContractTests
{
    [Fact]
    public void ReferenceAssemblyIsRevit2023AndDefinesNativeDocumentEquality()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "contracts", "RevitAPI.dll"));
        using var pe = new PEReader(stream); var metadata = pe.GetMetadataReader();
        Assert.Equal(23, metadata.GetAssemblyDefinition().Version.Major);
        var handle = metadata.TypeDefinitions.Single(h => metadata.GetString(metadata.GetTypeDefinition(h).Namespace) == "Autodesk.Revit.DB" && metadata.GetString(metadata.GetTypeDefinition(h).Name) == "Document");
        var document = metadata.GetTypeDefinition(handle);
        var equals = document.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == "Equals");
        Assert.True((equals.Attributes & MethodAttributes.Virtual) != 0);
        Assert.True((equals.Attributes & MethodAttributes.NewSlot) == 0);
        foreach (var h in metadata.TypeDefinitions.Where(h => metadata.GetString(metadata.GetTypeDefinition(h).Namespace) == "Autodesk.Revit.DB" && new[] { "Document", "APIObject" }.Contains(metadata.GetString(metadata.GetTypeDefinition(h).Name))))
        {
            var names = metadata.GetTypeDefinition(h).GetMethods().Select(metadata.GetMethodDefinition).Select(m => metadata.GetString(m.Name));
            Assert.DoesNotContain("op_Inequality", names); Assert.DoesNotContain("op_Equality", names);
        }
    }

    [Fact]
    public void Revit2023DocumentsTheSameOpenDocumentSemantics()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "contracts", "RevitAPI.xml"));
        var member = xml.Descendants("member").Single(m => (string?)m.Attribute("name") == "M:Autodesk.Revit.DB.Document.Equals(System.Object)");
        Assert.Contains("same document currently opened", member.Value);
        Assert.NotNull(xml.Descendants("member").SingleOrDefault(m => (string?)m.Attribute("name") == "M:Autodesk.Revit.DB.Document.GetElement(System.String)"));
        Assert.NotNull(xml.Descendants("member").SingleOrDefault(m => (string?)m.Attribute("name") == "P:Autodesk.Revit.DB.Element.UniqueId"));
    }

    [Fact]
    public void Revit2023ExposesDistinctNominalAndModelDiameters()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "contracts", "RevitAPI.xml"));
        foreach (var property in new[] { "BarNominalDiameter", "BarModelDiameter" })
            Assert.NotNull(xml.Descendants("member").SingleOrDefault(m => (string?)m.Attribute("name") == "P:Autodesk.Revit.DB.Structure.RebarBarType." + property));
    }

    [Fact]
    public void VersionGuidAloneIsNotAnInSessionChangeDetector()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "contracts", "RevitAPI.xml"));
        var version = xml.Descendants("member").Single(m => (string?)m.Attribute("name") == "P:Autodesk.Revit.DB.Element.VersionGuid");
        Assert.Contains("this version cannot be used to determine", version.Value);
    }
}
