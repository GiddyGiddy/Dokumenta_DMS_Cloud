using DocuManagementApp.Data;
using DocuManagementApp.Models;
using DocuManagementApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocuManagementApp.Tests;

public sealed class ApiPostgresRevisionFactAttribute : FactAttribute
{
    public ApiPostgresRevisionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_API_POSTGRES")))
        {
            Skip = "Set DOCUMENTA_TEST_API_POSTGRES to a disposable empty PostgreSQL database.";
        }
    }
}

public sealed class RevisionOutboxPostgresTests
{
    [ApiPostgresRevisionFact]
    public async Task RevisionAndOutboxAreCommittedAndClaimedWithOriginalCorrelation()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_API_POSTGRES"))
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var tenantId = $"tenant:{Guid.NewGuid():D}";
        var document = new DocumentRecord
        {
            TenantId = tenantId,
            OriginalFileName = "notes.txt",
            ContentType = "text/plain",
            FileContent = [1],
            SizeBytes = 1
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var result = await new DocumentRevisionService(db).CreateAsync(
            document.Id, tenantId, [2], "text/plain", 1, "editor", null);

        Assert.Equal(RevisionOutcome.Created, result.Outcome);
        var claimed = await new EfOutboxStore(db).ClaimDueAsync(10, TimeSpan.FromMinutes(1), CancellationToken.None);
        var message = Assert.Single(claimed.Where(m => m.DocumentId == document.Id));
        Assert.Equal(document.CorrelationId, message.CorrelationId);
        Assert.Equal("document.revised", message.EventType);
        Assert.NotNull(message.LockToken);
        Assert.Equal(2, (await db.DocumentRevisions.CountAsync(r => r.DocumentId == document.Id)));
    }
}
