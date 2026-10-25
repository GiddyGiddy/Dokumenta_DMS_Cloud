using System.Text.Json;

namespace ExtractionService;

public sealed record ExtractionRequest(
    Guid EventId,
    Guid CorrelationId,
    Guid DocumentId,
    Guid TaskId,
    string DocumentKind,
    string TenantId,
    string? InputReference,
    string RawPayload)
{
    public static ExtractionRequest Parse(ReadOnlyMemory<byte> body, string? messageId)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var eventType = ReadString(root, "eventType");
        if (!string.Equals(eventType, "document.task.requested", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported extraction event type '{eventType}'.");
        }

        var schemaVersion = ReadInt32(root, "schemaVersion");
        if (schemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported extraction event schema version '{schemaVersion}'.");
        }

        if (!string.Equals(ReadString(root, "taskType"), "extraction", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The task request is not an extraction task.");
        }

        var tenantId = ReadString(root, "tenantId");
        if (!IsTenantIdValid(tenantId))
        {
            throw new InvalidDataException("The extraction request is missing a valid tenantId.");
        }

        return new ExtractionRequest(
            ReadGuid(root, "eventId") ?? ParseGuid(messageId, "MessageId"),
            RequiredGuid(root, "correlationId"),
            RequiredGuid(root, "documentId"),
            RequiredGuid(root, "taskId"),
            ReadString(root, "documentKind") ?? "Unknown",
            tenantId!,
            ReadString(root, "inputReference"),
            JsonSerializer.Serialize(root));
    }

    private static Guid RequiredGuid(JsonElement element, string propertyName)
    {
        return ReadGuid(element, propertyName) ??
            throw new InvalidDataException($"The extraction request is missing a valid {propertyName}.");
    }

    private static Guid? ReadGuid(JsonElement element, string propertyName)
    {
        return Guid.TryParse(ReadString(element, propertyName), out var value) ? value : null;
    }

    private static Guid ParseGuid(string? value, string fieldName)
    {
        return Guid.TryParse(value, out var result)
            ? result
            : throw new InvalidDataException($"The request is missing a valid {fieldName}.");
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        return null;
    }

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsTenantIdValid(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var separator = value.IndexOf(':');
        var identity = separator < 0 ? string.Empty : value[(separator + 1)..];
        var isPersonal = separator >= 0 && value[..separator] == "personal";
        var validIdentity = Guid.TryParse(identity, out _) ||
            (isPersonal && identity.Length == 64 &&
             identity.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'));
        if (separator < 0 || !validIdentity ||
            value.IndexOf(':', separator + 1) >= 0)
        {
            return false;
        }

        return value[..separator] is "tenant" or "personal";
    }
}