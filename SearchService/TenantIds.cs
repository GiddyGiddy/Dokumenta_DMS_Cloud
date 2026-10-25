using System.Text.RegularExpressions;

namespace SearchService;

public static partial class TenantIds
{
    public const string HeaderName = "X-Tenant-Id";

    [GeneratedRegex("^(tenant|personal):[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && Pattern().IsMatch(value);

    public static string Require(string? value)
    {
        return IsValid(value) ? value! : throw new InvalidDataException("The tenant identifier is missing or malformed.");
    }

    public static bool TryRead(HttpRequest request, out string tenantId)
    {
        tenantId = request.Headers[HeaderName].ToString();
        return IsValid(tenantId);
    }
}
