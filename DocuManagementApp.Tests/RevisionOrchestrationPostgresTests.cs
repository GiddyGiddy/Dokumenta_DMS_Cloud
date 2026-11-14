using System.Text.Json;
using Npgsql;
using OrchestratorService;
using Xunit;

namespace DocuManagementApp.Tests;

public sealed class PostgresRevisionFactAttribute : FactAttribute
{
    public PostgresRevisionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_POSTGRES")))
        {
            Skip = "Set DOCUMENTA_TEST_POSTGRES to a disposable PostgreSQL database to run this integration test.";
        }
    }
}

public sealed class RevisionOrchestrationPostgresTests
{
    [PostgresRevisionFact]
    public async Task OutOfOrderRevisionsAndOldFailureDoNotDisruptNewestWorkflow()
    {
        await using var dataSource = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_POSTGRES")!);
        var database = new OrchestratorDatabase(dataSource, new DocumentClassifier());
        await database.InitializeAsync(CancellationToken.None);
        var documentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var tenantId = $"tenant:{Guid.NewGuid():D}";

        OrchestrationEvent Ingested(int revision) => new(
            Guid.NewGuid(), correlationId, revision == 1 ? "document.ingested" : "document.revised", 1,
            [new IngestedDocument(documentId, "notes.txt", "text/plain", 5, revision)],
            documentId, null, null, null, null, tenantId);

        await database.ProcessAsync(Ingested(1), CancellationToken.None);
        await database.ProcessAsync(Ingested(3), CancellationToken.None);
        await database.ProcessAsync(Ingested(2), CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var tasks = new NpgsqlCommand("""
            SELECT task_id, revision_number FROM document_tasks
            WHERE document_id = @id AND task_type = 'extraction' ORDER BY revision_number;
            """, connection);
        tasks.Parameters.AddWithValue("id", documentId);
        var ids = new Dictionary<int, Guid>();
        await using (var reader = await tasks.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) ids.Add(reader.GetInt32(1), reader.GetGuid(0));
        }
        Assert.Equal(new[] { 1, 3 }, ids.Keys.Order().ToArray());

        await database.ProcessAsync(new OrchestrationEvent(
            Guid.NewGuid(), correlationId, "document.task.failed", 1, [],
            documentId, ids[1], "extraction", null, "Old revision failed", tenantId), CancellationToken.None);
        await database.ProcessAsync(new OrchestrationEvent(
            Guid.NewGuid(), correlationId, "document.task.completed", 1, [],
            documentId, ids[3], "extraction", "http://example.test/artifact", null, tenantId), CancellationToken.None);

        await using var check = new NpgsqlCommand("""
            SELECT w.status, w.revision_number, t.task_id, o.payload::text
            FROM document_workflows w
            JOIN document_tasks t ON t.document_id = w.document_id AND t.task_type = 'search-index'
            JOIN orchestration_outbox o ON o.payload->>'taskId' = t.task_id::text
            WHERE w.document_id = @id;
            """, connection);
        check.Parameters.AddWithValue("id", documentId);
        Guid searchTaskId;
        await using (var reader = await check.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Processing", reader.GetString(0));
            Assert.Equal(3, reader.GetInt32(1));
            searchTaskId = reader.GetGuid(2);
            using var payload = JsonDocument.Parse(reader.GetString(3));
            Assert.Equal(3, payload.RootElement.GetProperty("revisionNumber").GetInt32());
            Assert.False(await reader.ReadAsync());
        }

        await database.ProcessAsync(new OrchestrationEvent(
            Guid.NewGuid(), correlationId, "document.task.completed", 1, [],
            documentId, searchTaskId, "search-index", "http://example.test/artifact", null, tenantId), CancellationToken.None);
        await using var status = new NpgsqlCommand("SELECT status FROM document_workflows WHERE document_id = @id", connection);
        status.Parameters.AddWithValue("id", documentId);
        Assert.Equal("Completed", await status.ExecuteScalarAsync());
    }
}
