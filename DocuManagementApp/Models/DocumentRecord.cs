namespace DocuManagementApp.Models;

public sealed class DocumentRecord
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid CorrelationId { get; set; } = Guid.NewGuid();
  public string? TenantId { get; set; }
  public string OriginalFileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = "application/octet-stream";
  public byte[] FileContent { get; set; } = Array.Empty<byte>();
  // Set when the bytes live in Blob Storage; FileContent is then empty.
  public string? BlobName { get; set; }
  public long SizeBytes { get; set; }
  public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public DocumentProcessingStatus ProcessingStatus { get; set; } = DocumentProcessingStatus.Completed;
  public DocumentOutputFormat RequestedOutputFormat { get; set; } = DocumentOutputFormat.Original;
  public DateTimeOffset? ProcessingStartedAtUtc { get; set; }
  public DateTimeOffset? ProcessingCompletedAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public string? FailureSummary { get; set; }
  public int CurrentRevisionNumber { get; set; }
}
