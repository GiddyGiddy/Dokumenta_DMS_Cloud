using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DocuManagementApp.Services;

public static class TenantKey
{
    // Every personal Microsoft account shares this issuer tenant, so it cannot be used as an isolation boundary.
    public const string PersonalAccountTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";

    public static string? FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var tenantId = FirstClaim(principal, "tid", "http://schemas.microsoft.com/identity/claims/tenantid");
        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return null;
        }

        if (!string.Equals(tenantGuid.ToString("D"), PersonalAccountTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return "tenant:" + tenantGuid.ToString("D");
        }

        var userId = FirstClaim(principal, "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier");
        if (Guid.TryParse(userId, out var userGuid))
        {
            return "personal:" + userGuid.ToString("D");
        }

        var subject = FirstClaim(principal, "sub");
        return string.IsNullOrWhiteSpace(subject)
            ? null
            : "personal:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject))).ToLowerInvariant();
    }

    public static bool IsValidKey(string? value) =>
        value is not null &&
        (Regex.IsMatch(value, @"^tenant:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$") ||
         Regex.IsMatch(value, @"^personal:(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-f]{64})$"));

    private static string? FirstClaim(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
