using DocuManagementApp.Data;
using DocuManagementApp.Models;
using Microsoft.EntityFrameworkCore;

namespace DocuManagementApp.Services
{

public sealed class DocumentListItem
{
  public string StoredFileName { get; set; } = string.Empty;
  public Guid CorrelationId { get; set; }
  public string OriginalFileName { get; set; } = string.Empty;
  public long Size { get; set; }
  public DateTimeOffset UploadedAt { get; set; }
  public string ProcessingStatus { get; set; } = string.Empty;
  public string RequestedOutputFormat { get; set; } = string.Empty;
  public DateTimeOffset? ProcessingStartedAt { get; set; }
  public DateTimeOffset? ProcessingCompletedAt { get; set; }
  public string? FailureSummary { get; set; }
}

public sealed class StoredDocumentResult
{
  public Guid Id { get; set; }
  public Guid CorrelationId { get; set; }
  public string OriginalFileName { get; set; } = string.Empty;
  public long Size { get; set; }
}

public sealed class DocumentDownloadResult
{
  public Guid Id { get; set; }
  public Guid CorrelationId { get; set; }
  public string OriginalFileName { get; set; } = string.Empty;
  public string ContentType { get; set; } = "application/octet-stream";
  public byte[] Content { get; set; } = Array.Empty<byte>();
}

public interface IDocumentStorageService
{
  Task<IReadOnlyList<DocumentListItem>> GetDocumentsAsync(CancellationToken cancellationToken);
  Task<DocumentDownloadResult?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DocumentStorageService : IDocumentStorageService
{
  private readonly AppDbContext _dbContext;
  private readonly IDocumentBlobStore _blobStore;

  public DocumentStorageService(AppDbContext dbContext, IDocumentBlobStore? blobStore = null)
  {
    _dbContext = dbContext;
    _blobStore = blobStore ?? new DisabledDocumentBlobStore();
  }

  // Shared so the download endpoint can also fix up legacy octet-stream uploads by extension.
  public static string ResolveContentTypeForExtension(string fileName)
  {
    return Path.GetExtension(fileName).ToLowerInvariant() switch
    {
      ".pdf" => "application/pdf",
      ".txt" => "text/plain",
      ".doc" => "application/msword",
      ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      ".xls" => "application/vnd.ms-excel",
      ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      ".png" => "image/png",
      ".jpg" or ".jpeg" => "image/jpeg",
      ".gif" => "image/gif",
      ".bmp" => "image/bmp",
      ".mp4" => "video/mp4",
      ".mkv" => "video/x-matroska",
      ".mp3" => "audio/mpeg",
      _ => "application/octet-stream"
    };
  }

  public async Task<IReadOnlyList<DocumentListItem>> GetDocumentsAsync(CancellationToken cancellationToken)
  {
    return await _dbContext.Documents
      .OrderByDescending(x => x.UploadedAtUtc)
      .Select(x => new DocumentListItem
      {
        StoredFileName = x.Id.ToString(),
        CorrelationId = x.CorrelationId,
        OriginalFileName = x.OriginalFileName,
        Size = x.SizeBytes,
        UploadedAt = x.UploadedAtUtc,
        ProcessingStatus = x.ProcessingStatus.ToString(),
        RequestedOutputFormat = x.RequestedOutputFormat.ToString(),
        ProcessingStartedAt = x.ProcessingStartedAtUtc,
        ProcessingCompletedAt = x.ProcessingCompletedAtUtc,
        FailureSummary = x.FailureSummary
      })
      .ToListAsync(cancellationToken);
  }

  public async Task<DocumentDownloadResult?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken)
  {
    var row = await _dbContext.Documents
      .AsNoTracking()
      .Where(x => x.Id == id)
      .Select(x => new { x.Id, x.CorrelationId, x.OriginalFileName, x.ContentType, x.FileContent, x.BlobName })
      .SingleOrDefaultAsync(cancellationToken);
    if (row is null) return null;

    var content = string.IsNullOrEmpty(row.BlobName)
      ? row.FileContent
      : await _blobStore.ReadAsync(row.BlobName, cancellationToken);
    return new DocumentDownloadResult
    {
      Id = row.Id,
      CorrelationId = row.CorrelationId,
      OriginalFileName = row.OriginalFileName,
      ContentType = row.ContentType,
      Content = content
    };
  }
}
}
