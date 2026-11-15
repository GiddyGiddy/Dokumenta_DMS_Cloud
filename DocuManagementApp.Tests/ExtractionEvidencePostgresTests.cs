using ExtractionService;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace DocuManagementApp.Tests;

public sealed class PostgresExtractionFactAttribute : FactAttribute
{
    public PostgresExtractionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_EXTRACTION_POSTGRES")))
        {
            Skip = "Set DOCUMENTA_TEST_EXTRACTION_POSTGRES to a disposable empty PostgreSQL database.";
        }
    }
}

public sealed class ExtractionEvidencePostgresTests
{
    [PostgresExtractionFact]
    public async Task PersistsSourceValuesAndReadsLegacyArtifacts()
    {
        await using var dataSource = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("DOCUMENTA_TEST_EXTRACTION_POSTGRES")!);
        var store = new ExtractionDatabase(dataSource, Options.Create(new ExtractionArtifactApiOptions()));
        await store.InitializeAsync(CancellationToken.None);
        var taskId = Guid.NewGuid();
        var request = new ExtractionRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), taskId,
            "Invoice", $"tenant:{Guid.NewGuid():D}", null, "{}");
        await store.StoreRequestAsync(request, CancellationToken.None);
        var job = await store.ClaimNextJobAsync(TimeSpan.FromMinutes(5), true, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(taskId, job.TaskId);
        var layout = new DocumentLayoutResult("Gesamt 23,80 €",
            """{"analyzeResult":{"paragraphs":[{"content":"Gesamt 23,80 €","boundingRegions":[{"pageNumber":1}]}]}}""");
        var outcome = new ExtractionSuccess(1, "Invoice", """{"total":23.80}""")
        {
            NormalizedSourceValues = new Dictionary<string, string> { ["total"] = "23,80" }
        };
        await store.MarkExtractionOutcomeAsync(job, layout, outcome, CancellationToken.None);
        var artifact = await store.GetArtifactAsync(taskId, CancellationToken.None);
        Assert.NotNull(artifact);
        Assert.Equal("23,80", Assert.Single(artifact.NormalizedValueEvidence).SourceText);
        Assert.Equal(23.80m, artifact.ExtractionResult.GetProperty("total").GetDecimal());

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("UPDATE extraction_jobs SET normalized_source_values = NULL WHERE task_id = @id", connection);
        command.Parameters.AddWithValue("id", taskId);
        await command.ExecuteNonQueryAsync();
        var legacy = await store.GetArtifactAsync(taskId, CancellationToken.None);
        Assert.NotNull(legacy);
        Assert.Empty(legacy.NormalizedValueEvidence);
    }
}
