using DocuManagementApp.Data;
using DocuManagementApp.Models;
using DocuManagementApp.Services;
using Microsoft.EntityFrameworkCore;

namespace DocuManagementApp.Tests;

public sealed class DocumentStorageTenantTests
{
    [Fact]
    public async Task ListAndDownloadOnlyReturnDocumentsOwnedByRequestedTenant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var tenantA = $"tenant:{Guid.NewGuid():D}";
        var tenantB = $"tenant:{Guid.NewGuid():D}";
        var documentA = new DocumentRecord
        {
            TenantId = tenantA,
            OriginalFileName = "tenant-a.txt",
            ContentType = "text/plain",
            FileContent = [1],
            SizeBytes = 1
        };
        var documentB = new DocumentRecord
        {
            TenantId = tenantB,
            OriginalFileName = "tenant-b.txt",
            ContentType = "text/plain",
            FileContent = [2],
            SizeBytes = 1
        };
        var legacyDocument = new DocumentRecord
        {
            OriginalFileName = "legacy-unassigned.txt",
            ContentType = "text/plain",
            FileContent = [3],
            SizeBytes = 1
        };
        db.Documents.AddRange(documentA, documentB, legacyDocument);
        await db.SaveChangesAsync();

        var storage = new DocumentStorageService(db);

        var listed = await storage.GetDocumentsAsync(tenantA, CancellationToken.None);
        var ownDownload = await storage.GetDocumentByIdAsync(documentA.Id, tenantA, CancellationToken.None);
        var foreignDownload = await storage.GetDocumentByIdAsync(documentB.Id, tenantA, CancellationToken.None);

        Assert.Equal("tenant-a.txt", Assert.Single(listed).OriginalFileName);
        Assert.Equal(new byte[] { 1 }, ownDownload!.Content);
        Assert.Null(foreignDownload);
    }
}
