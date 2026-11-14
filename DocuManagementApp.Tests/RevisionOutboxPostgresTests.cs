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
    public async Task CompetingApprovalsPublishOnlyOneRevision()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_API_POSTGRES")).Options;
        var tenant = $"tenant:{Guid.NewGuid():D}";
        Guid documentId;
        await using (var setup = new AppDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var document = new DocumentRecord { TenantId = tenant, ContentType = "text/plain", FileContent = [1], SizeBytes = 1 };
            setup.Documents.Add(document);
            await setup.SaveChangesAsync();
            documentId = document.Id;
            var revisions = new DocumentRevisionService(setup);
            await revisions.CreateAsync(documentId, tenant, [2], null, 1, "a", "first");
            await revisions.CreateAsync(documentId, tenant, [3], null, 1, "b", "second");
            await revisions.TransitionAsync(documentId, 2, tenant, "submit", null, "a", "a");
            await revisions.TransitionAsync(documentId, 3, tenant, "submit", null, "b", "b");
        }

        async Task<RevisionResult> Approve(int number)
        {
            await using var db = new AppDbContext(options);
            return await new DocumentRevisionService(db).TransitionAsync(documentId, number, tenant, "approve", null, "reviewer", "reviewer");
        }

        var results = await Task.WhenAll(Approve(2), Approve(3));
        Assert.Single(results.Where(r => r.Outcome == RevisionOutcome.Created));
        Assert.Single(results.Where(r => r.Outcome == RevisionOutcome.Conflict));
        await using var check = new AppDbContext(options);
        Assert.Equal(1, await check.OutboxMessages.CountAsync(m => m.DocumentId == documentId));
        Assert.Equal(1, await check.DocumentRevisions.CountAsync(r => r.DocumentId == documentId && r.RevisionNumber > 1 && r.Status == "Approved"));
    }

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

        var revisions = new DocumentRevisionService(db);
        var result = await revisions.CreateAsync(
            document.Id, tenantId, [2], "text/plain", 1, "editor", "edit");

        Assert.Equal(RevisionOutcome.Created, result.Outcome);
        Assert.Empty(await new EfOutboxStore(db).ClaimDueAsync(10, TimeSpan.FromMinutes(1), CancellationToken.None));
        await revisions.TransitionAsync(document.Id, 2, tenantId, "submit", null, "editor", "editor");
        await revisions.TransitionAsync(document.Id, 2, tenantId, "approve", null, "reviewer", "reviewer");
        var claimed = await new EfOutboxStore(db).ClaimDueAsync(10, TimeSpan.FromMinutes(1), CancellationToken.None);
        var message = Assert.Single(claimed.Where(m => m.DocumentId == document.Id));
        Assert.Equal(document.CorrelationId, message.CorrelationId);
        Assert.Equal("document.revised", message.EventType);
        Assert.NotNull(message.LockToken);
        Assert.Equal(2, (await db.DocumentRevisions.CountAsync(r => r.DocumentId == document.Id)));
    }
}
