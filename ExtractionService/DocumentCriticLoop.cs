using Microsoft.Extensions.Options;

namespace ExtractionService;

public interface IDocumentExtractionAgent
{
    Task<string> ExtractAsync(string documentKind, string markdown, CancellationToken cancellationToken);
}

public interface IDocumentClassifierAgent
{
    Task<DocumentClassification> ClassifyAsync(string markdown, CancellationToken cancellationToken);
}

public interface IDocumentCorrectionAgent
{
    Task<string> CorrectAsync(
        string documentKind,
        string markdown,
        string candidateJson,
        IReadOnlyList<ExtractionValidationIssue> issues,
        CancellationToken cancellationToken);
}

public sealed class DocumentCriticLoop(
    IDocumentExtractionAgent extractionAgent,
    IDocumentCorrectionAgent correctionAgent,
    DocumentExtractionValidator validator,
    IOptions<ExtractionProcessingOptions>? options = null)
{
    private const int MaximumAttempts = 3;

    public async Task<DocumentExtractionOutcome> RunAsync(string documentKind, string markdown, CancellationToken cancellationToken)
    {
        var normalizedKind = DocumentExtractionSchemas.NormalizeKind(documentKind);
        var candidateJson = await extractionAgent.ExtractAsync(normalizedKind, markdown, cancellationToken);
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var requireSourceValues = options?.Value.RequireNormalizedSourceValues ?? true;
            IReadOnlyList<ExtractionValidationIssue> issues = requireSourceValues
                ? DocumentNormalizedSourceValues.Validate(candidateJson, markdown, normalizedKind)
                : [];
            var normalizedJson = candidateJson;
            if (issues.Count == 0)
            {
                normalizedJson = validator.Normalize(normalizedKind, candidateJson);
                issues = validator.Validate(normalizedKind, normalizedJson);
            }
            if (issues.Count == 0 && (options?.Value.RequireSourceTextEvidence ?? true))
            {
                issues = DocumentSourceEvidence.Validate(normalizedJson, markdown);
            }
            if (issues.Count == 0)
            {
                return new ExtractionSuccess(attempt, normalizedKind, normalizedJson)
                {
                    NormalizedSourceValues = requireSourceValues ? DocumentNormalizedSourceValues.Extract(candidateJson) : null
                };
            }

            if (attempt == MaximumAttempts)
            {
                return new ExtractionFailed(attempt, normalizedKind, candidateJson, issues);
            }

            candidateJson = await correctionAgent.CorrectAsync(normalizedKind, markdown, candidateJson, issues, cancellationToken);
        }

        throw new InvalidOperationException("The invoice critic loop exited without a result.");
    }
}