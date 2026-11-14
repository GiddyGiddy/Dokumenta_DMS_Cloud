using DocuManagementApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocuManagementApp.Controllers;

/// <summary>Creates a comment or replies to an existing thread.</summary>
public sealed record CreateRevisionCommentRequest(Guid? ParentId, int StartOffset, string SelectedText, string Text);
/// <summary>Changes the resolution state of a thread.</summary>
public sealed record ResolveRevisionCommentRequest(bool Resolved);

[ApiController]
[Route("api/documents/{documentId:guid}/revisions")]
[Authorize(Policy = "DmsRead")]
public sealed class RevisionCollaborationController(IRevisionCollaborationService collaboration) : ControllerBase
{
  [HttpGet("activity")]
  public async Task<IActionResult> Activities(Guid documentId, CancellationToken ct)
  {
    var tenant = TenantKey.FromPrincipal(User);
    if (!TenantKey.IsValidKey(tenant)) return Forbid();
    var result = await collaboration.ActivitiesAsync(documentId, tenant!, ct);
    return result is null ? NotFound() : Ok(result);
  }

  [HttpGet("{revisionNumber:int}/comments")]
  public async Task<IActionResult> Comments(Guid documentId, int revisionNumber, CancellationToken ct)
  {
    var tenant = TenantKey.FromPrincipal(User);
    if (!TenantKey.IsValidKey(tenant)) return Forbid();
    var result = await collaboration.CommentsAsync(documentId, revisionNumber, tenant!, ct);
    return result is null ? NotFound() : Ok(result);
  }

  [HttpPost("{revisionNumber:int}/comments")]
  [Authorize(Policy = "DmsWrite")]
  public async Task<IActionResult> AddComment(Guid documentId, int revisionNumber, CreateRevisionCommentRequest request, CancellationToken ct)
  {
    var tenant = TenantKey.FromPrincipal(User);
    var actorId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value;
    if (!TenantKey.IsValidKey(tenant) || string.IsNullOrWhiteSpace(actorId)) return Forbid();
    try
    {
      var result = await collaboration.AddCommentAsync(documentId, revisionNumber, tenant!, request.ParentId,
        request.StartOffset, request.SelectedText, request.Text, actorId, ActorName(), ct);
      return result is null ? NotFound() : CreatedAtAction(nameof(Comments), new { documentId, revisionNumber }, result);
    }
    catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException
      { SqlState: "23505", ConstraintName: "IX_document_revisions_DocumentId_RevisionNumber" })
    {
      return Conflict(new { message = "The revision was created concurrently. Reload and retry the comment." });
    }
  }

  [HttpPost("{revisionNumber:int}/comments/{commentId:guid}/resolution")]
  [Authorize(Policy = "DmsWrite")]
  public async Task<IActionResult> Resolve(Guid documentId, int revisionNumber, Guid commentId, ResolveRevisionCommentRequest request, CancellationToken ct)
  {
    var tenant = TenantKey.FromPrincipal(User);
    var actorId = User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value;
    if (!TenantKey.IsValidKey(tenant) || string.IsNullOrWhiteSpace(actorId)) return Forbid();
    return await collaboration.ResolveAsync(documentId, revisionNumber, tenant!, commentId, request.Resolved, actorId, ActorName(), ct)
      ? NoContent() : NotFound();
  }

  private string ActorName() => User.FindFirst("preferred_username")?.Value ?? User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "unknown";
}
