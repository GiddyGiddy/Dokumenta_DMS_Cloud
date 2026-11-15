using ExtractionService;

namespace DocuManagementApp.Tests;

public sealed class DocumentNormalizedSourceValuesTests
{
    [Fact]
    public void AcceptsGermanAndEnglishSourceValuesAndRetainsThem()
    {
        const string candidate = """
            {"invoiceDate":"15.03.2024","currency":"€","subtotal":"1.234,56","tax":"0","total":"1,234.56",
             "lineItems":[{"quantity":"2","unitPrice":"617,28","lineTotal":"1.234,56"}]}
            """;
        const string source = "15.03.2024 € 1.234,56 0 1,234.56 2 617,28";

        var issues = DocumentNormalizedSourceValues.Validate(candidate, source, SupportedDocumentKinds.Invoice);

        Assert.Empty(issues);
        var values = DocumentNormalizedSourceValues.Extract(candidate);
        Assert.Equal("1.234,56", values["subtotal"]);
        Assert.Equal("617,28", values["lineItems[0].unitPrice"]);
    }

    [Theory]
    [InlineData("1.234", "1.234")]
    [InlineData("1,234", "1,234")]
    [InlineData("1", "1.234")]
    [InlineData("234", "1.234")]
    [InlineData("123", "(123)")]
    [InlineData("123", "123-")]
    [InlineData("10", "20")]
    [InlineData("1,2.3", "1,2.3")]
    public void RejectsAmbiguousUnsupportedOrPartialNumbers(string raw, string source)
    {
        var candidate = System.Text.Json.JsonSerializer.Serialize(new { revenue = raw });
        var issues = DocumentNormalizedSourceValues.Validate(candidate, source, SupportedDocumentKinds.FinancialStatement);
        Assert.Contains(issues, issue => issue.Path == "revenue");
    }

    [Theory]
    [InlineData("03/04/2026")]
    [InlineData("15.03.24")]
    public void RejectsAmbiguousDates(string raw)
    {
        var candidate = System.Text.Json.JsonSerializer.Serialize(new { issueDate = raw });
        Assert.Contains(DocumentNormalizedSourceValues.Validate(candidate, raw, SupportedDocumentKinds.IdentityDocument),
            issue => issue.Path == "issueDate");
    }

    [Fact]
    public void RejectsNumbersAlreadyConvertedByTheModelAndAmbiguousCurrency()
    {
        const string candidate = """{"revenue":1234,"currency":"$"}""";
        var issues = DocumentNormalizedSourceValues.Validate(candidate, "$1,234", SupportedDocumentKinds.FinancialStatement);
        Assert.Contains(issues, issue => issue.Path == "revenue");
        Assert.Contains(issues, issue => issue.Path == "currency");
    }

    [Fact]
    public void RejectsMissingInvoiceAmountsInsteadOfDefaultingToZero()
    {
        const string candidate = """{"lineItems":[{"quantity":"1","unitPrice":"10","lineTotal":"10"}],"subtotal":"10","total":"10"}""";
        var issues = DocumentNormalizedSourceValues.Validate(candidate, "1 10", SupportedDocumentKinds.Invoice);
        Assert.Contains(issues, issue => issue.Path == "tax");
    }

    [Fact]
    public void AcceptsSentencePunctuationAndAdjacentCurrencySymbol()
    {
        const string candidate = """{"revenue":"23,80","currency":"€","periodStart":"15.03.2024"}""";
        Assert.Empty(DocumentNormalizedSourceValues.Validate(candidate, "15.03.2024. Gesamt 23,80€.",
            SupportedDocumentKinds.FinancialStatement));
    }

    [Fact]
    public void ChecksTranslatedAliasesAndCaseInsensitiveLineItems()
    {
        const string candidate = """
            {"zwischensumme":"10","steuer":"0","gesamtbetrag":"10",
             "Positionen":[{"menge":"1","einzelpreis":"10"}]}
            """;
        var issues = DocumentNormalizedSourceValues.Validate(candidate, "0 1 10", SupportedDocumentKinds.Invoice);
        Assert.Contains(issues, issue => issue.Path == "lineItems[0].lineTotal");
        var values = DocumentNormalizedSourceValues.Extract(candidate);
        Assert.Equal("10", values["subtotal"]);
        Assert.Equal("1", values["lineItems[0].quantity"]);
        Assert.Contains(DocumentNormalizedSourceValues.Validate("""{"umsatz":1234}""", "1.234",
            SupportedDocumentKinds.FinancialStatement), issue => issue.Path == "revenue");
    }

    [Theory]
    [InlineData("""{"currency":null,"subtotal":"0","tax":"0","total":"0","lineItems":[]}""", "currency")]
    [InlineData("""{"subtotal":"0","tax":"0","total":"0","lineItems":null}""", "lineItems")]
    [InlineData("""{"subtotal":"0","tax":"0","total":"0","lineItems":[null]}""", "lineItems[0]")]
    [InlineData("""{"subtotal":"0","tax":"0","total":"0","lineItems":[],"LineItems":[]}""", "lineItems")]
    public void RejectsMalformedRequiredInvoiceValues(string candidate, string path)
    {
        Assert.Contains(DocumentNormalizedSourceValues.Validate(candidate, "0", SupportedDocumentKinds.Invoice),
            issue => issue.Path == path);
    }
}
