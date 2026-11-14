using System.Text.Json;
using Npgsql;
using SearchService;
using Xunit;

namespace DocuManagementApp.Tests;

public sealed class SearchPostgresRevisionFactAttribute : FactAttribute
{
    public SearchPostgresRevisionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_SEARCH_POSTGRES")))
        {
            Skip = "Set DOCUMENTA_TEST_SEARCH_POSTGRES to a disposable PostgreSQL database with pgvector.";
        }
    }
}

public sealed class RevisionSearchPostgresTests
{
    [SearchPostgresRevisionFact]
    public async Task OlderRevisionCannotOverwriteNewerSearchIndex()
    {
        await using var dataSource = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_SEARCH_POSTGRES")!);
        var database = new SearchDatabase(dataSource, new SearchOptions { EmbeddingDimensions = 3 });
        await database.InitializeAsync(CancellationToken.None);
        var documentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var tenantId = $"tenant:{Guid.NewGuid():D}";
        using var json = JsonDocument.Parse("{}");
        var artifact = new ExtractionArtifactResponse(Guid.NewGuid(), correlationId, documentId, "Text",
            "text", json.RootElement.Clone(), json.RootElement.Clone(), 1, DateTimeOffset.UtcNow);

        SearchIndexCommand Command(int revision) => new(Guid.NewGuid(), correlationId, documentId,
            Guid.NewGuid(), "Text", "http://example.test/artifact", tenantId, revision);

        await database.IndexAsync(Command(3), artifact, [new SearchChunk(0, "new content", 1, null)], null, CancellationToken.None);
        await database.IndexAsync(Command(2), artifact, [new SearchChunk(0, "old content", 1, null)], null, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT d.revision_number, c.chunk_text
            FROM search_documents d JOIN search_chunks c
                ON c.tenant_id = d.tenant_id AND c.document_id = d.document_id
            WHERE d.tenant_id = @tenant_id AND d.document_id = @document_id;
            """, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("document_id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal("new content", reader.GetString(1));
        Assert.False(await reader.ReadAsync());
    }
}
