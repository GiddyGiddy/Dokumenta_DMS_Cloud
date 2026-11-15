using ExtractionService;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DocuManagementApp.Tests;

public sealed class DocumentCriticLoopTests
{
    [Fact]
    public async Task CorrectsInvalidInvoiceTotalsAndReturnsValidatedJson()
    {
        var correctionAgent = new FakeCorrectionAgent([ValidInvoiceJson]);
        var loop = CreateLoop(new FakeExtractionAgent(InvalidTotalJson), correctionAgent);

        var result = await loop.RunAsync(SupportedDocumentKinds.Invoice, "# Invoice\nExample Supplies INV-100 Paper", CancellationToken.None);

        var success = Assert.IsType<ExtractionSuccess>(result);
        Assert.Equal(SupportedDocumentKinds.Invoice, success.DocumentKind);
        Assert.Equal(2, success.Attempts);
        Assert.Contains("\"total\": 10.00", success.Json);
        Assert.Single(correctionAgent.RequestedIssues);
        Assert.Contains(correctionAgent.RequestedIssues[0], issue => issue.Path == "total");
    }

    [Fact]
    public async Task ReturnsFailureAfterThreeTotalAttempts()
    {
        var correctionAgent = new FakeCorrectionAgent([InvalidTotalJson, InvalidTotalJson]);
        var loop = CreateLoop(new FakeExtractionAgent(InvalidTotalJson), correctionAgent);

        var result = await loop.RunAsync(SupportedDocumentKinds.Invoice, "# Invoice\nExample Supplies INV-100 Paper", CancellationToken.None);

        var failed = Assert.IsType<ExtractionFailed>(result);
        Assert.Equal(3, failed.Attempts);
        Assert.Equal(2, correctionAgent.RequestedIssues.Count);
        Assert.Contains(failed.Issues, issue => issue.Path == "total");
    }

    [Fact]
    public async Task RejectsUnexpectedJsonProperties()
    {
        var payload = ValidInvoiceJson.Replace("\"vendorName\"", "\"untrustedExtra\":\"value\",\"vendorName\"", StringComparison.Ordinal);
        var loop = CreateLoop(new FakeExtractionAgent(payload), new FakeCorrectionAgent([ValidInvoiceJson]));

        var result = await loop.RunAsync(SupportedDocumentKinds.Invoice, "# Invoice\nExample Supplies INV-100 Paper", CancellationToken.None);

        var success = Assert.IsType<ExtractionSuccess>(result);
        Assert.Equal(2, success.Attempts);
    }

    [Fact]
    public async Task RejectsTranslatedTextThatDoesNotAppearInTheSource()
    {
        var loop = CreateLoop(new FakeExtractionAgent(ValidInvoiceJson), new FakeCorrectionAgent([ValidInvoiceJson, ValidInvoiceJson]));
        var result = await loop.RunAsync(SupportedDocumentKinds.Invoice, "# Rechnung\nExample Supplies INV-100 Papier", CancellationToken.None);

        var failed = Assert.IsType<ExtractionFailed>(result);
        Assert.Contains(failed.Issues, issue => issue.Path == "lineItems[0].description");
    }

    [Fact]
    public async Task SourceTextCheckCanBeDisabledForRollback()
    {
        var loop = new DocumentCriticLoop(new FakeExtractionAgent(ValidInvoiceJson),
            new FakeCorrectionAgent([ValidInvoiceJson]), new DocumentExtractionValidator(),
            Options.Create(new ExtractionProcessingOptions { RequireSourceTextEvidence = false, RequireNormalizedSourceValues = false }));

        var result = await loop.RunAsync(SupportedDocumentKinds.Invoice, "# Rechnung", CancellationToken.None);

        Assert.IsType<ExtractionSuccess>(result);
    }

    [Fact]
    public async Task NormalizedValueCheckCanBeDisabledForRollback()
    {
        var loop = new DocumentCriticLoop(new FakeExtractionAgent(ValidInvoiceJson),
            new FakeCorrectionAgent([ValidInvoiceJson]), new DocumentExtractionValidator(),
            Options.Create(new ExtractionProcessingOptions { RequireNormalizedSourceValues = false }));

        var result = Assert.IsType<ExtractionSuccess>(await loop.RunAsync("Invoice",
            "Example Supplies INV-100 Paper", CancellationToken.None));

        Assert.Null(result.NormalizedSourceValues);
    }

    [Fact]
    public async Task PreservesOriginalValuesAndProducesNormalizedInvoice()
    {
        const string candidate = """
            {"vendorName":"Muster GmbH","invoiceNumber":"RE-1","invoiceDate":"15.03.2024","currency":"€",
             "lineItems":[{"description":"Beratung","quantity":"2","unitPrice":"10,00","lineTotal":"20,00"}],
             "subtotal":"20,00","tax":"3,80","total":"23,80"}
            """;
        const string source = "Muster GmbH RE-1 Beratung 15.03.2024 € 2 10,00 20,00 3,80 23,80.";
        var loop = new DocumentCriticLoop(new FakeExtractionAgent(candidate),
            new FakeCorrectionAgent([candidate]), new DocumentExtractionValidator());

        var result = Assert.IsType<ExtractionSuccess>(await loop.RunAsync("Invoice", source, CancellationToken.None));

        using var normalized = JsonDocument.Parse(result.Json);
        Assert.Equal(23.80m, normalized.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal("2024-03-15", normalized.RootElement.GetProperty("invoiceDate").GetString());
        Assert.Equal("EUR", normalized.RootElement.GetProperty("currency").GetString());
        Assert.Equal("23,80", result.NormalizedSourceValues!["total"]);
        Assert.Equal("15.03.2024", result.NormalizedSourceValues["invoiceDate"]);
    }

    [Fact]
    public async Task DoesNotAcceptAConvertedValueWithoutOriginalEvidence()
    {
        var loop = new DocumentCriticLoop(new FakeExtractionAgent(ValidInvoiceJson),
            new FakeCorrectionAgent([ValidInvoiceJson, ValidInvoiceJson]), new DocumentExtractionValidator());
        var result = await loop.RunAsync("Invoice",
            "Example Supplies INV-100 Paper 2026-10-01 2026-10-31 USD 1 10.00 0.00", CancellationToken.None);

        var failed = Assert.IsType<ExtractionFailed>(result);
        Assert.Contains(failed.Issues, issue => issue.Path == "total");
    }

    [Fact]
    public async Task CorrectsEnglishSourceValuesAndRetainsEvidence()
    {
        const string corrected = """
            {"vendorName":"Example Supplies","invoiceNumber":"INV-100","invoiceDate":"12/31/2026","currency":"USD",
             "lineItems":[{"description":"Paper","quantity":"1","unitPrice":"1,234.56","lineTotal":"1,234.56"}],
             "subtotal":"1,234.56","tax":"0.00","total":"1,234.56"}
            """;
        const string source = "Example Supplies INV-100 Paper 12/31/2026 USD 1 1,234.56 0.00";
        var correction = new FakeCorrectionAgent([corrected]);
        var loop = new DocumentCriticLoop(new FakeExtractionAgent(ValidInvoiceJson), correction, new DocumentExtractionValidator());

        var result = Assert.IsType<ExtractionSuccess>(await loop.RunAsync("Invoice", source, CancellationToken.None));

        Assert.Equal(2, result.Attempts);
        Assert.Contains(correction.RequestedIssues[0], issue => issue.Path == "total");
        using var normalized = JsonDocument.Parse(result.Json);
        Assert.Equal(1234.56m, normalized.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal("2026-12-31", normalized.RootElement.GetProperty("invoiceDate").GetString());
        Assert.Equal("1,234.56", result.NormalizedSourceValues!["total"]);
    }

    private static DocumentCriticLoop CreateLoop(FakeExtractionAgent extraction, FakeCorrectionAgent correction)
    {
        return new DocumentCriticLoop(extraction, correction, new DocumentExtractionValidator(),
            Options.Create(new ExtractionProcessingOptions { RequireNormalizedSourceValues = false }));
    }

    internal const string InvalidTotalJson = """
        {
          "vendorName": "Example Supplies",
          "invoiceNumber": "INV-100",
          "invoiceDate": "2026-10-01",
          "dueDate": "2026-10-31",
          "currency": "USD",
          "lineItems": [{ "description": "Paper", "quantity": 1, "unitPrice": 10.00, "lineTotal": 10.00 }],
          "subtotal": 10.00,
          "tax": 0.00,
          "total": 11.00
        }
        """;

    internal const string ValidInvoiceJson = """
        {
          "vendorName": "Example Supplies",
          "invoiceNumber": "INV-100",
          "invoiceDate": "2026-10-01",
          "dueDate": "2026-10-31",
          "currency": "USD",
          "lineItems": [{ "description": "Paper", "quantity": 1, "unitPrice": 10.00, "lineTotal": 10.00 }],
          "subtotal": 10.00,
          "tax": 0.00,
          "total": 10.00
        }
        """;

    private sealed class FakeExtractionAgent(string response) : IDocumentExtractionAgent
    {
        public Task<string> ExtractAsync(string documentKind, string markdown, CancellationToken cancellationToken)
        {
            Assert.Equal(SupportedDocumentKinds.Invoice, documentKind);
            return Task.FromResult(response);
        }
    }

    private sealed class FakeCorrectionAgent(IReadOnlyList<string> responses) : IDocumentCorrectionAgent
    {
        private int _callCount;

        public List<IReadOnlyList<ExtractionValidationIssue>> RequestedIssues { get; } = [];

        public Task<string> CorrectAsync(
            string documentKind,
            string markdown,
            string candidateJson,
            IReadOnlyList<ExtractionValidationIssue> issues,
            CancellationToken cancellationToken)
        {
            Assert.Equal(SupportedDocumentKinds.Invoice, documentKind);
            RequestedIssues.Add(issues);
            var response = responses[Math.Min(_callCount, responses.Count - 1)];
            _callCount++;
            return Task.FromResult(response);
        }
    }
}