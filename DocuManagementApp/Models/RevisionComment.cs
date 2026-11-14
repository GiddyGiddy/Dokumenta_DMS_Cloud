namespace DocuManagementApp.Models;

public sealed class RevisionComment
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid RevisionId { get; set; }
  public Guid? ParentId { get; set; }
  public string ActorId { get; set; } = string.Empty;
  public string ActorName { get; set; } = string.Empty;
  public string Text { get; set; } = string.Empty;
  public int StartOffset { get; set; }
  public string SelectedText { get; set; } = string.Empty;
  public bool Resolved { get; set; }
  public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
