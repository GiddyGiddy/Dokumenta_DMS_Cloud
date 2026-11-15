using ExtractionService;

namespace DocuManagementApp.Tests;

public class GermanCandidateNormalizerTests
{
    [Fact]
    public void GermanInvoiceWithLocaleFormattingValidates()
    {
        const string json = """
            {"vendorName":"Muster GmbH","invoiceNumber":"RE-1","invoiceDate":"15.03.2024","dueDate":"14.04.2024",
             "currency":"€","lineItems":[{"description":"Beratung","quantity":"2","unitPrice":"1.000,50","lineTotal":"2.001,00"}],
             "subtotal":"2.001,00","tax":"380,19","total":"2.381,19"}
            """;
        var validator = new DocumentExtractionValidator();

        Assert.Empty(validator.Validate("Invoice", json));
        Assert.Contains("\"2024-03-15\"", validator.Normalize("Invoice", json));
    }

    [Fact]
    public void AcceptsGrossPricedLineItems()
    {
        const string json = """
            {"vendorName":"DB Fernverkehr AG","invoiceNumber":"1","invoiceDate":"2025-06-15","currency":"EUR",
             "lineItems":[{"description":"Fahrkarte","quantity":1,"unitPrice":37.99,"lineTotal":37.99}],
             "subtotal":35.5,"tax":2.49,"total":37.99}
            """;

        Assert.Empty(new DocumentExtractionValidator().Validate("Invoice", json));
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("12,5", 12.5)]
    [InlineData("-1.000,00 €", -1000)]
    [InlineData("(250,00)", -250)]
    public void ParsesNumbers(string raw, double expected)
    {
        Assert.True(GermanCandidateNormalizer.TryParseNumber(raw, out var number));
        Assert.Equal((decimal)expected, number);
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("1,234")]
    public void AmbiguousNumbersAreNotGuessed(string raw)
    {
        Assert.False(GermanCandidateNormalizer.TryParseNumber(raw, out _));
    }

    [Theory]
    [InlineData("03/04/2026")]
    [InlineData("03.04.2026")]
    [InlineData("15.03.24")]
    public void AmbiguousDatesAreNotGuessed(string raw)
    {
        Assert.False(GermanCandidateNormalizer.TryParseDate(raw, out _));
    }

    [Fact]
    public void DollarSymbolDoesNotImplyUsCurrency()
    {
        Assert.NotEqual("USD", GermanCandidateNormalizer.NormalizeCurrency("$"));
    }
}
