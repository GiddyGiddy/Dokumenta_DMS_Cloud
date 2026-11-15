using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExtractionService;

public static class DocumentNormalizedSourceValues
{
    private static readonly HashSet<string> DateFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "invoiceDate", "dueDate", "effectiveDate", "expirationDate", "issueDate",
        "expiryDate", "dateOfBirth", "periodStart", "periodEnd"
    };

    private static readonly HashSet<string> NumberFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "subtotal", "tax", "total", "quantity", "unitPrice", "lineTotal",
        "revenue", "expenses", "netIncome", "assets", "liabilities", "equity"
    };

    private static readonly Regex NumberSyntax = new(
        @"^(?:\d+|\d+[.,]\d+|\d{1,3}(?:,\d{3}){2,}|\d{1,3}(?:\.\d{3}){2,}|\d{1,3}(?:,\d{3})+\.\d+|\d{1,3}(?:\.\d{3})+,\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CurrencyCode = new(@"^[A-Za-z]{3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<ExtractionValidationIssue> Validate(string candidateJson, string markdown, string kind)
    {
        JsonDocument candidate;
        try
        {
            candidate = JsonDocument.Parse(candidateJson);
        }
        catch (JsonException)
        {
            return [new("$", "The model returned invalid JSON.")];
        }

        using (candidate)
        {
            var issues = new List<ExtractionValidationIssue>();
            if (candidate.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [new("$", "The extraction must be a JSON object.")];
            }

            if (kind == SupportedDocumentKinds.Invoice)
            {
                RequireProperties(candidate.RootElement, "", ["subtotal", "tax", "total"], issues);
                var itemProperties = candidate.RootElement.EnumerateObject()
                    .Where(property => GermanCandidateNormalizer.CanonicalPropertyName(property.Name) == "lineItems").ToArray();
                if (itemProperties.Length > 1)
                {
                    issues.Add(new("lineItems", "The field appears more than once."));
                }
                foreach (var property in itemProperties)
                {
                    var items = property.Value;
                    if (items.ValueKind != JsonValueKind.Array)
                    {
                        issues.Add(new("lineItems", "Line items must be an array."));
                        continue;
                    }
                    var index = 0;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            RequireProperties(item, $"lineItems[{index}]", ["quantity", "unitPrice", "lineTotal"], issues);
                        }
                        else
                        {
                            issues.Add(new($"lineItems[{index}]", "A line item must be an object."));
                        }
                        index++;
                    }
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, field, value) in EnumerateValues(candidate.RootElement, ""))
            {
                if (!seen.Add(path))
                {
                    issues.Add(new(path, "The field appears more than once."));
                    continue;
                }
                if (value.ValueKind == JsonValueKind.Null &&
                    (kind != SupportedDocumentKinds.Invoice || (!NumberFields.Contains(field) && field != "currency")))
                {
                    continue;
                }
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                {
                    issues.Add(new(path, "Provide the original source value as a non-empty string."));
                    continue;
                }

                var raw = value.GetString()!;
                if (raw != raw.Trim() || !ContainsToken(markdown, raw))
                {
                    issues.Add(new(path, "The complete original value was not found in the source."));
                    continue;
                }

                var valid = DateFields.Contains(field)
                    ? GermanCandidateNormalizer.TryParseDate(raw, out _)
                    : NumberFields.Contains(field)
                        ? IsUnambiguousNumber(raw)
                        : raw is "€" or "£" || CurrencyCode.IsMatch(raw);
                if (!valid)
                {
                    issues.Add(new(path, "The source value is ambiguous or has an unsupported format."));
                }
            }
            return issues;
        }
    }

    public static IReadOnlyDictionary<string, string> Extract(string candidateJson)
    {
        using var candidate = JsonDocument.Parse(candidateJson);
        return EnumerateValues(candidate.RootElement, "")
            .Where(item => item.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(item => item.Path, item => item.Value.GetString()!, StringComparer.Ordinal);
    }

    private static bool IsUnambiguousNumber(string raw)
    {
        var unsigned = raw.Replace('\u2212', '-');
        if (unsigned.StartsWith('(') && unsigned.EndsWith(')'))
        {
            unsigned = unsigned[1..^1];
        }
        else if (unsigned.StartsWith('-'))
        {
            unsigned = unsigned[1..];
        }
        return NumberSyntax.IsMatch(unsigned) && GermanCandidateNormalizer.TryParseNumber(raw, out _);
    }

    private static bool ContainsToken(string source, string raw)
    {
        var start = 0;
        while (start <= source.Length - raw.Length)
        {
            var index = source.IndexOf(raw, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }
            var end = index + raw.Length;
            var numeric = char.IsDigit(raw[0]) || raw[0] is '-' or '\u2212' or '(';
            var symbol = raw is "€" or "£";
            var parenthesizedUnsigned = numeric && char.IsDigit(raw[0]) && index > 0 && end < source.Length &&
                source[index - 1] == '(' && source[end] == ')';
            var leftIsPartOfValue = parenthesizedUnsigned || index > 0 &&
                !symbol &&
                (char.IsLetterOrDigit(source[index - 1]) ||
                 (numeric && (source[index - 1] is '.' or ',' or '/' or '-' or '+' or '\u2212')));
            var rightIsPartOfValue = end < source.Length &&
                !symbol &&
                (char.IsLetterOrDigit(source[end]) ||
                 (numeric && (source[end] is '-' or '\u2212')) ||
                 (numeric && (source[end] is '.' or ',' or '/' or '-' or '+') &&
                  end + 1 < source.Length && char.IsDigit(source[end + 1])));
            if (!leftIsPartOfValue && !rightIsPartOfValue)
            {
                return true;
            }
            start = index + 1;
        }
        return false;
    }

    private static void RequireProperties(JsonElement obj, string parent, string[] fields, List<ExtractionValidationIssue> issues)
    {
        foreach (var field in fields)
        {
            if (!obj.EnumerateObject().Any(property => GermanCandidateNormalizer.CanonicalPropertyName(property.Name) == field))
            {
                issues.Add(new(parent.Length == 0 ? field : $"{parent}.{field}", "The source value is required; do not assume zero."));
            }
        }
    }

    private static IEnumerable<(string Path, string Field, JsonElement Value)> EnumerateValues(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = GermanCandidateNormalizer.CanonicalPropertyName(property.Name);
                var childPath = path.Length == 0 ? name : $"{path}.{name}";
                if (DateFields.Contains(name) || NumberFields.Contains(name) || name == "currency")
                {
                    yield return (childPath, name, property.Value);
                }
                else
                {
                    foreach (var child in EnumerateValues(property.Value, childPath))
                    {
                        yield return child;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray())
            {
                foreach (var item in EnumerateValues(child, $"{path}[{index}]"))
                {
                    yield return item;
                }
                index++;
            }
        }
    }
}
