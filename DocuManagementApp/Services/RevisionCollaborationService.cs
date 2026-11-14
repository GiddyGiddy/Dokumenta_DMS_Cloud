using System.Text;
using System.Text.Json;
using DocuManagementApp.Data;
using DocuManagementApp.Models;
using Microsoft.EntityFrameworkCore;

namespace DocuManagementApp.Services;

/// <summary>A chronological revision activity.</summary>
public sealed record RevisionActivityResponse(Guid Id, int RevisionNumber, string ActorId, string ActorName, string Action, string? Reason, string? Changes, DateTimeOffset CreatedAtUtc);

/// <summary>A comment anchored to an immutable revision.</summary>
public sealed record RevisionCommentResponse(Guid Id, Guid? ParentId, string ActorName, string Text, int StartOffset, string SelectedText, bool Resolved, DateTimeOffset CreatedAtUtc);

public interface IRevisionCollaborationService
{
  Task<IReadOnlyList<RevisionActivityResponse>?> ActivitiesAsync(Guid documentId, string tenantId, CancellationToken ct);
  Task<IReadOnlyList<RevisionCommentResponse>?> CommentsAsync(Guid documentId, int revisionNumber, string tenantId, CancellationToken ct);
  Task<RevisionCommentResponse?> AddCommentAsync(Guid documentId, int revisionNumber, string tenantId, Guid? parentId, int startOffset, string selectedText, string text, string actorId, string actorName, CancellationToken ct);
  Task<bool> ResolveAsync(Guid documentId, int revisionNumber, string tenantId, Guid commentId, bool resolved, string actorId, string actorName, CancellationToken ct);
}

public sealed class RevisionCollaborationService(AppDbContext db, IDocumentRevisionService revisions) : IRevisionCollaborationService
{
  public async Task<IReadOnlyList<RevisionActivityResponse>?> ActivitiesAsync(Guid documentId, string tenantId, CancellationToken ct)
  {
    if (!await db.Documents.AnyAsync(d => d.Id == documentId && d.TenantId == tenantId, ct)) return null;
    return await db.RevisionActivities.AsNoTracking().Where(a => a.DocumentId == documentId)
      .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
      .Select(a => new RevisionActivityResponse(a.Id, a.RevisionNumber, a.ActorId, a.ActorName, a.Action, a.Reason, a.Changes, a.CreatedAtUtc))
      .ToListAsync(ct);
  }

  public async Task<IReadOnlyList<RevisionCommentResponse>?> CommentsAsync(Guid documentId, int revisionNumber, string tenantId, CancellationToken ct)
  {
    var revision = await FindAsync(documentId, revisionNumber, tenantId, ct);
    if (revision is null)
      return await revisions.GetAsync(documentId, revisionNumber, tenantId, ct) is null ? null : [];
    return await db.RevisionComments.AsNoTracking().Where(c => c.RevisionId == revision.Id)
      .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
      .Select(c => new RevisionCommentResponse(c.Id, c.ParentId, c.ActorName, c.Text, c.StartOffset, c.SelectedText, c.Resolved, c.CreatedAtUtc))
      .ToListAsync(ct);
  }

  public async Task<RevisionCommentResponse?> AddCommentAsync(Guid documentId, int revisionNumber, string tenantId, Guid? parentId, int startOffset, string selectedText, string text, string actorId, string actorName, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new ArgumentException("Comment must contain 1 to 2000 characters.");
    var revision = await FindAsync(documentId, revisionNumber, tenantId, ct);
    var syntheticOriginal = false;
    if (revision is null && revisionNumber == 1 && parentId is null)
    {
      revision = await revisions.GetAsync(documentId, revisionNumber, tenantId, ct);
      syntheticOriginal = revision is not null;
    }
    if (revision is null) return null;
    if (!IsText(revision.ContentType)) throw new ArgumentException("Comments are only supported on text documents.");
    if (parentId is not null)
    {
      var parent = await db.RevisionComments.FirstOrDefaultAsync(c => c.Id == parentId && c.RevisionId == revision.Id && c.ParentId == null, ct);
      if (parent is null) throw new ArgumentException("The comment thread does not exist.");
      if (parent.Resolved) throw new ArgumentException("The comment thread is resolved.");
      startOffset = parent.StartOffset;
      selectedText = parent.SelectedText;
    }
    else
    {
      if (string.IsNullOrEmpty(selectedText) || selectedText.Length > 4000 || startOffset < 0)
        throw new ArgumentException("A valid text selection is required.");
      var content = await revisions.GetAsync(documentId, revisionNumber, tenantId, ct);
      if (content is null) return null;
      string source;
      try { source = new UTF8Encoding(false, true).GetString(content.FileContent); }
      catch (DecoderFallbackException) { throw new ArgumentException("The document is not valid UTF-8 text."); }
      if (startOffset > source.Length || selectedText.Length > source.Length - startOffset ||
          !source.AsSpan(startOffset, selectedText.Length).SequenceEqual(selectedText.AsSpan()))
        throw new ArgumentException("The selected passage does not match this revision.");
    }
    var comment = new RevisionComment
    {
      RevisionId = revision.Id, ParentId = parentId, ActorId = actorId, ActorName = actorName,
      Text = text.Trim(), StartOffset = startOffset, SelectedText = selectedText
    };
    if (syntheticOriginal)
    {
      if (!string.IsNullOrEmpty(revision.BlobName)) revision.FileContent = Array.Empty<byte>();
      db.DocumentRevisions.Add(revision);
    }
    db.RevisionComments.Add(comment);
    AddActivity(documentId, revisionNumber, actorId, actorName, parentId is null ? "comment_added" : "comment_replied", comment.Id);
    await db.SaveChangesAsync(ct);
    return new RevisionCommentResponse(comment.Id, comment.ParentId, comment.ActorName, comment.Text, comment.StartOffset, comment.SelectedText, comment.Resolved, comment.CreatedAtUtc);
  }

  public async Task<bool> ResolveAsync(Guid documentId, int revisionNumber, string tenantId, Guid commentId, bool resolved, string actorId, string actorName, CancellationToken ct)
  {
    var revision = await FindAsync(documentId, revisionNumber, tenantId, ct);
    if (revision is null) return false;
    var comment = await db.RevisionComments.FirstOrDefaultAsync(c => c.Id == commentId && c.RevisionId == revision.Id && c.ParentId == null, ct);
    if (comment is null) return false;
    if (comment.Resolved == resolved) return true;
    comment.Resolved = resolved;
    AddActivity(documentId, revisionNumber, actorId, actorName, resolved ? "comment_resolved" : "comment_reopened", comment.Id);
    await db.SaveChangesAsync(ct);
    return true;
  }

  private Task<DocumentRevision?> FindAsync(Guid documentId, int revisionNumber, string tenantId, CancellationToken ct) =>
    db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == documentId && r.RevisionNumber == revisionNumber &&
      db.Documents.Any(d => d.Id == documentId && d.TenantId == tenantId), ct);

  private static bool IsText(string type) =>
    type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
    type.Contains("json", StringComparison.OrdinalIgnoreCase) || type.Contains("xml", StringComparison.OrdinalIgnoreCase);

  private void AddActivity(Guid documentId, int revisionNumber, string actorId, string actorName, string action, Guid commentId) =>
    db.RevisionActivities.Add(new RevisionActivity
    {
      DocumentId = documentId, RevisionNumber = revisionNumber, ActorId = actorId, ActorName = actorName,
      Action = action, Changes = JsonSerializer.Serialize(new { commentId })
    });
}
