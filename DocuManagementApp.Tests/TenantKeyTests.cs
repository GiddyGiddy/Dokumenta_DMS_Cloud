using System.Security.Claims;
using DocuManagementApp.Services;

namespace DocuManagementApp.Tests;

public sealed class TenantKeyTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));
    }

    [Fact]
    public void WorkAccountsAreIsolatedByTenantId()
    {
        var tenant = Guid.NewGuid();
        var key = TenantKey.FromPrincipal(Principal(("tid", tenant.ToString()), ("oid", Guid.NewGuid().ToString())));

        Assert.Equal($"tenant:{tenant:D}", key);
    }

    [Fact]
    public void PersonalAccountsAreIsolatedPerUser()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var firstKey = TenantKey.FromPrincipal(Principal(("tid", TenantKey.PersonalAccountTenantId), ("oid", first.ToString())));
        var secondKey = TenantKey.FromPrincipal(Principal(("tid", TenantKey.PersonalAccountTenantId), ("oid", second.ToString())));

        Assert.Equal($"personal:{first:D}", firstKey);
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void PersonalSubjectFallbackIsStableAndProducesValidTenantKey()
    {
        var principal = Principal(("tid", TenantKey.PersonalAccountTenantId), ("sub", "app-scoped-subject"));

        var firstKey = TenantKey.FromPrincipal(principal);
        var secondKey = TenantKey.FromPrincipal(principal);

        Assert.Equal(firstKey, secondKey);
        Assert.True(TenantKey.IsValidKey(firstKey));
    }

    [Fact]
    public void MissingOrInvalidTenantClaimYieldsNoKey()
    {
        Assert.Null(TenantKey.FromPrincipal(Principal(("oid", Guid.NewGuid().ToString()))));
        Assert.Null(TenantKey.FromPrincipal(Principal(("tid", "not-a-guid"))));
        Assert.Null(TenantKey.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(TenantKey.FromPrincipal(null));
    }
}
