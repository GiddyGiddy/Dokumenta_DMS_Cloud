namespace DocuManagementApp.Models;

public sealed class RevisionActivity
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid DocumentId { get; set; }
  public int RevisionNumber { get; set; }
  public string ActorId { get; set; } = string.Empty;
  public string ActorName { get; set; } = string.Empty;
  public string Action { get; set; } = string.Empty;
  public string? Reason { get; set; }
  public string? Changes { get; set; }
  public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
