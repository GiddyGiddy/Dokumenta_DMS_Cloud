using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DocuManagementApp.Services;

// Stores document bytes outside the database. When no store is configured, content stays in the DB column.
public interface IDocumentBlobStore
{
  bool IsEnabled { get; }
  Task<string> SaveAsync(Guid documentId, byte[] content, string contentType, CancellationToken ct);
  Task<byte[]> ReadAsync(string blobName, CancellationToken ct);
}

public sealed class DisabledDocumentBlobStore : IDocumentBlobStore
{
  public bool IsEnabled => false;

  public Task<string> SaveAsync(Guid documentId, byte[] content, string contentType, CancellationToken ct) =>
    throw new InvalidOperationException("Blob storage is not configured.");

  public Task<byte[]> ReadAsync(string blobName, CancellationToken ct) =>
    throw new InvalidOperationException("Blob storage is not configured.");
}

public sealed class BlobStorageOptions
{
  public string ConnectionString { get; set; } = string.Empty;
  // Used with managed identity when ConnectionString is empty, e.g. https://<account>.blob.core.windows.net
  public string AccountUrl { get; set; } = string.Empty;
  public string ContainerName { get; set; } = "documents";

  public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) || !string.IsNullOrWhiteSpace(AccountUrl);
}

public sealed class AzureDocumentBlobStore : IDocumentBlobStore
{
  private readonly BlobContainerClient _container;
  private readonly SemaphoreSlim _initLock = new(1, 1);
  private bool _initialized;

  public AzureDocumentBlobStore(BlobStorageOptions options)
  {
    _container = !string.IsNullOrWhiteSpace(options.ConnectionString)
      ? new BlobContainerClient(options.ConnectionString, options.ContainerName)
      : new BlobContainerClient(
          new Uri($"{options.AccountUrl.TrimEnd('/')}/{options.ContainerName}"),
          new DefaultAzureCredential());
  }

  public bool IsEnabled => true;

  public async Task<string> SaveAsync(Guid documentId, byte[] content, string contentType, CancellationToken ct)
  {
    await EnsureContainerAsync(ct);
    // Blobs are immutable: every revision gets its own name so revisions can share or replace content safely.
    var blobName = $"{documentId:N}/{Guid.NewGuid():N}";
    var blob = _container.GetBlobClient(blobName);
    using var stream = new MemoryStream(content, writable: false);
    await blob.UploadAsync(stream, new BlobUploadOptions
    {
      HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
    }, ct);
    return blobName;
  }

  public async Task<byte[]> ReadAsync(string blobName, CancellationToken ct)
  {
    var result = await _container.GetBlobClient(blobName).DownloadContentAsync(ct);
    return result.Value.Content.ToArray();
  }

  private async Task EnsureContainerAsync(CancellationToken ct)
  {
    if (_initialized) return;
    await _initLock.WaitAsync(ct);
    try
    {
      if (_initialized) return;
      await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
      _initialized = true;
    }
    finally
    {
      _initLock.Release();
    }
  }
}
