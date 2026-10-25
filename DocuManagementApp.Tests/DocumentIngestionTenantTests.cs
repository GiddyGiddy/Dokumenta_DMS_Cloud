using System.Text.Json;
using DocuManagementApp.Data;
using DocuManagementApp.Models;
using DocuManagementApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DocuManagementApp.Tests;

public sealed class DocumentIngestionTenantTests
{
    [Fact]
    public async Task IngestionStoresTenantAndCarriesItIntoTheOutboxEvent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new AppDbContext(options);
        var tenantId = $"tenant:{Guid.NewGuid():D}";
        var ingestion = new DocumentIngestionService(db);

        var result = await ingestion.IngestAsync(
            tenantId,
            null,
            [new IngestionDocumentInput("contract.txt", [1, 2], "text/plain", DocumentOutputFormat.Original)],
            DocumentOutputFormat.Original,
            CancellationToken.None);

        var stored = await db.Documents.SingleAsync();
        var outbox = await db.OutboxMessages.SingleAsync();
        using var payload = JsonDocument.Parse(outbox.Payload);

        Assert.Equal(tenantId, stored.TenantId);
        Assert.Equal(tenantId, payload.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(result.DocumentId, stored.Id);
    }
}
