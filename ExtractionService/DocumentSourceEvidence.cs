using System.Text.Json;

namespace ExtractionService;

public sealed record SourceTextEvidence(string Path, string SourceText, IReadOnlyList<int> PageNumbers);

public static class DocumentSourceEvidence
{
    private static readonly HashSet<string> TextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "vendorName", "invoiceNumber", "description", "contractType", "governingLaw",
        "terminationClause", "documentType", "fullName", "documentNumber", "issuingCountry",
        "entityName", "statementType", "title", "name", "value"
    };

    private static readonly HashSet<string> TextArrays = new(StringComparer.OrdinalIgnoreCase)
    {
        "parties", "keyObligations"
    };

    public static IReadOnlyList<ExtractionValidationIssue> Validate(string candidateJson, string markdown)
    {
        using var candidate = JsonDocument.Parse(candidateJson);
        return EnumerateText(candidate.RootElement, "", false)
            .Where(item => !markdown.Contains(item.Text, StringComparison.Ordinal))
            .Select(item => new ExtractionValidationIssue(item.Path, "Text is not present verbatim in the source document."))
            .ToArray();
    }

    public static IReadOnlyList<SourceTextEvidence> Collect(string candidateJson, string markdown, JsonElement layout)
    {
        using var candidate = JsonDocument.Parse(candidateJson);
        var paragraphs = ReadParagraphs(layout);
        return EnumerateText(candidate.RootElement, "", false)
            .Where(item => markdown.Contains(item.Text, StringComparison.Ordinal))
            .Select(item => new SourceTextEvidence(
                item.Path,
                item.Text,
                paragraphs.Where(paragraph => paragraph.Text.Contains(item.Text, StringComparison.Ordinal))
                    .SelectMany(paragraph => paragraph.Pages).Distinct().Order().ToArray()))
            .ToArray();
    }

    public static IReadOnlyList<SourceTextEvidence> CollectNormalizedValues(
        IReadOnlyDictionary<string, string> sourceValues, JsonElement layout)
    {
        var paragraphs = ReadParagraphs(layout);
        return sourceValues.Select(item => new SourceTextEvidence(
            item.Key,
            item.Value,
            paragraphs.Where(paragraph => paragraph.Text.Contains(item.Value, StringComparison.Ordinal))
                .SelectMany(paragraph => paragraph.Pages).Distinct().Order().ToArray()))
            .ToArray();
    }

    private static IEnumerable<(string Path, string Text)> EnumerateText(JsonElement value, string path, bool includeString)
    {
        if (value.ValueKind == JsonValueKind.String && includeString)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return (path, text);
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                foreach (var item in EnumerateText(property.Value, childPath,
                    TextFields.Contains(property.Name) || TextArrays.Contains(property.Name)))
                {
                    yield return item;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in value.EnumerateArray())
            {
                foreach (var item in EnumerateText(child, $"{path}[{index}]", includeString))
                {
                    yield return item;
                }
                index++;
            }
        }
    }

    private static IReadOnlyList<(string Text, IReadOnlyList<int> Pages)> ReadParagraphs(JsonElement layout)
    {
        if (layout.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        if (layout.TryGetProperty("analyzeResult", out var analyzeResult))
        {
            layout = analyzeResult;
        }
        if (layout.ValueKind != JsonValueKind.Object ||
            !layout.TryGetProperty("paragraphs", out var paragraphs) || paragraphs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<(string, IReadOnlyList<int>)>();
        foreach (var paragraph in paragraphs.EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Object ||
                !paragraph.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            var pages = new List<int>();
            if (paragraph.TryGetProperty("boundingRegions", out var regions) && regions.ValueKind == JsonValueKind.Array)
            {
                foreach (var region in regions.EnumerateArray())
                {
                    if (region.ValueKind == JsonValueKind.Object && region.TryGetProperty("pageNumber", out var page) &&
                        page.ValueKind == JsonValueKind.Number &&
                        page.TryGetInt32(out var number) && number > 0)
                    {
                        pages.Add(number);
                    }
                }
            }
            result.Add((content.GetString()!, pages));
        }
        return result;
    }
}
