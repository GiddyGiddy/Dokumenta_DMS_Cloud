using DocuManagementApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocuManagementApp.Controllers;

public sealed class CreateRevisionRequest
{
  public string ContentBase64 { get; set; } = string.Empty;
  public string? ContentType { get; set; }
  public int ExpectedRevision { get; set; }
  public string? Comment { get; set; }
}

public sealed class RestoreRevisionRequest
{
  public int ExpectedRevision { get; set; }
  public string Reason { get; set; } = string.Empty;
}

/// <summary>Optional explanation for a review decision.</summary>
public sealed record RevisionTransitionRequest(string? Reason);

[ApiController]
[Route("api/documents/{documentId:guid}/revisions")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "DmsRead")]
public sealed class DocumentRevisionsController : ControllerBase
{
  private readonly IDocumentRevisionService _revisions;

  public DocumentRevisionsController(IDocumentRevisionService revisions) => _revisions = revisions;

  [HttpGet]
  public async Task<IActionResult> List(Guid documentId, CancellationToken ct)
  {
    var tenantId = TenantKey.FromPrincipal(User);
    if (tenantId is null || !TenantKey.IsValidKey(tenantId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "The access token does not carry a usable tenant identity." });
    }

    var list = await _revisions.ListAsync(documentId, tenantId, ct);
    return list is null ? NotFound() : Ok(list);
  }

  [HttpGet("{revisionNumber:int}/content")]
  public async Task<IActionResult> Content(Guid documentId, int revisionNumber, CancellationToken ct)
  {
    var tenantId = TenantKey.FromPrincipal(User);
    if (tenantId is null || !TenantKey.IsValidKey(tenantId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "The access token does not carry a usable tenant identity." });
    }

    var rev = await _revisions.GetAsync(documentId, revisionNumber, tenantId, ct);
    return rev is null ? NotFound() : File(rev.FileContent, rev.ContentType);
  }

  [AllowAnonymous]
  [HttpGet("{revisionNumber:int}/service-content")]
  public async Task<IActionResult> ServiceContent(Guid documentId, int revisionNumber, [FromServices] IConfiguration configuration, CancellationToken ct)
  {
    var expected = configuration["ServiceDownload:ApiKey"];
    var provided = Request.Headers["X-Service-Key"].ToString();
    if (string.IsNullOrWhiteSpace(expected) ||
        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
          System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(provided)))
    {
      return Unauthorized();
    }

    var tenantId = Request.Headers["X-Tenant-Id"].ToString();
    if (!TenantKey.IsValidKey(tenantId))
    {
      return BadRequest(new { message = "A valid X-Tenant-Id header is required." });
    }

    var revision = await _revisions.GetAsync(documentId, revisionNumber, tenantId, ct);
    return revision is null || revision.Status != "Approved" ? NotFound() : File(revision.FileContent, revision.ContentType);
  }

  [HttpPost]
  [Authorize(Policy = "DmsWrite")]
  [RequestSizeLimit(30 * 1024 * 1024)]
  public async Task<IActionResult> Create(Guid documentId, [FromBody] CreateRevisionRequest request, CancellationToken ct)
  {
    var tenantId = TenantKey.FromPrincipal(User);
    if (tenantId is null || !TenantKey.IsValidKey(tenantId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "The access token does not carry a usable tenant identity." });
    }

    byte[] content;
    try
    {
      content = Convert.FromBase64String(request.ContentBase64 ?? string.Empty);
    }
    catch (FormatException)
    {
      return BadRequest("contentBase64 is not valid base64.");
    }

    var actorId = ActorId();
    if (string.IsNullOrWhiteSpace(actorId)) return Forbid();
    var result = await _revisions.CreateAsync(documentId, tenantId, content, request.ContentType,
      request.ExpectedRevision, Editor(), request.Comment, ct, actorId);
    return CreatedRevision(documentId, result);
  }

  [HttpPost("{revisionNumber:int}/restore")]
  [Authorize(Policy = "DmsReview")]
  public async Task<IActionResult> Restore(Guid documentId, int revisionNumber, [FromBody] RestoreRevisionRequest request, CancellationToken ct)
  {
    var tenantId = TenantKey.FromPrincipal(User);
    if (tenantId is null || !TenantKey.IsValidKey(tenantId))
    {
      return StatusCode(StatusCodes.Status403Forbidden, new { message = "The access token does not carry a usable tenant identity." });
    }

    var actorId = ActorId();
    if (string.IsNullOrWhiteSpace(actorId)) return Forbid();
    var result = await _revisions.RestoreAsync(documentId, revisionNumber, tenantId, request.ExpectedRevision, Editor(), request.Reason, ct, actorId);
    return CreatedRevision(documentId, result);
  }

  [HttpPost("{revisionNumber:int}/submit")]
  [Authorize(Policy = "DmsWrite")]
  public Task<IActionResult> Submit(Guid documentId, int revisionNumber, CancellationToken ct) =>
    Transition(documentId, revisionNumber, "submit", null, ct);

  [HttpPost("{revisionNumber:int}/approve")]
  [Authorize(Policy = "DmsReview")]
  public Task<IActionResult> Approve(Guid documentId, int revisionNumber, [FromBody] RevisionTransitionRequest request, CancellationToken ct) =>
    Transition(documentId, revisionNumber, "approve", request.Reason, ct);

  [HttpPost("{revisionNumber:int}/reject")]
  [Authorize(Policy = "DmsReview")]
  public Task<IActionResult> Reject(Guid documentId, int revisionNumber, [FromBody] RevisionTransitionRequest request, CancellationToken ct) =>
    Transition(documentId, revisionNumber, "reject", request.Reason, ct);

  private async Task<IActionResult> Transition(Guid documentId, int revisionNumber, string action, string? reason, CancellationToken ct)
  {
    var tenantId = TenantKey.FromPrincipal(User);
    var actorId = ActorId();
    if (tenantId is null || !TenantKey.IsValidKey(tenantId) || string.IsNullOrWhiteSpace(actorId)) return Forbid();
    var result = await _revisions.TransitionAsync(documentId, revisionNumber, tenantId, action, reason, actorId, Editor(), ct);
    return result.Outcome == RevisionOutcome.Created ? Ok(result.Revision) : Map(result);
  }

  private string? ActorId() => User.FindFirst("oid")?.Value ?? User.FindFirst("sub")?.Value;

  private string Editor() =>
    User.FindFirst("preferred_username")?.Value
    ?? User.FindFirst("name")?.Value
    ?? User.Identity?.Name
    ?? "unknown";

  private IActionResult CreatedRevision(Guid documentId, RevisionResult result) =>
    result.Outcome == RevisionOutcome.Created && result.Revision is not null
      ? CreatedAtAction(nameof(Content), new { documentId, revisionNumber = result.Revision.RevisionNumber }, result.Revision)
      : Map(result);

  private IActionResult Map(RevisionResult result) => result.Outcome switch
  {
    RevisionOutcome.Created => StatusCode(StatusCodes.Status201Created, result.Revision),
    RevisionOutcome.NotFound => NotFound(),
    RevisionOutcome.Invalid => BadRequest(new { message = "Content and a reason are required, and reasons must not exceed 2000 characters." }),
    _ => Conflict(new { message = "Document was modified by someone else.", currentRevision = result.CurrentRevision })
  };
}
