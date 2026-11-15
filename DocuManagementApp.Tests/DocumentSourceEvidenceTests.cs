using System.Text.Json;
using ExtractionService;

namespace DocuManagementApp.Tests;

public sealed class DocumentSourceEvidenceTests
{
    [Fact]
    public void FindsOriginalTextAndPageWithoutTranslatingMixedLanguageValues()
    {
        const string candidate = """{"vendorName":"Muster GmbH","invoiceNumber":"R-1","lineItems":[{"description":"Papier / paper","quantity":1,"unitPrice":1,"lineTotal":1}]}""";
        const string markdown = "Muster GmbH\nR-1\nPapier / paper";
        using var layout = JsonDocument.Parse("""
            {"analyzeResult":{"paragraphs":[
              {"content":"Muster GmbH","boundingRegions":[{"pageNumber":1}]},
              {"content":"Papier / paper","boundingRegions":[{"pageNumber":2}]}
            ]}}
            """);

        var evidence = DocumentSourceEvidence.Collect(candidate, markdown, layout.RootElement);

        Assert.Contains(evidence, item => item.Path == "vendorName" && item.SourceText == "Muster GmbH" && item.PageNumbers.SequenceEqual([1]));
        Assert.Contains(evidence, item => item.Path == "lineItems[0].description" && item.PageNumbers.SequenceEqual([2]));
        Assert.Contains(evidence, item => item.Path == "invoiceNumber" && item.PageNumbers.Count == 0);
    }

    [Fact]
    public void ReportsUnsupportedTextWithoutInventingEvidence()
    {
        const string candidate = """{"vendorName":"Translated company","invoiceNumber":"R-1"}""";
        var issues = DocumentSourceEvidence.Validate(candidate, "Firma Beispiel\nR-1");

        Assert.Contains(issues, issue => issue.Path == "vendorName");
        Assert.DoesNotContain(issues, issue => issue.Path == "invoiceNumber");
    }

    [Fact]
    public void NormalizedValueEvidenceRetainsSourceAndAvailablePages()
    {
        using var layout = JsonDocument.Parse("""
            {"analyzeResult":{"paragraphs":[{"content":"Gesamt 23,80 €","boundingRegions":[{"pageNumber":2}]}]}}
            """);
        var evidence = DocumentSourceEvidence.CollectNormalizedValues(
            new Dictionary<string, string> { ["total"] = "23,80" }, layout.RootElement);

        var value = Assert.Single(evidence);
        Assert.Equal("23,80", value.SourceText);
        Assert.Equal([2], value.PageNumbers);
    }
}
