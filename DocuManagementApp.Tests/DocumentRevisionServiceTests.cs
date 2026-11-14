using System.Text;
using System.Text.Json;
using DocuManagementApp.Data;
using DocuManagementApp.Models;
using DocuManagementApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrchestratorService;
using Xunit;

namespace DocuManagementApp.Tests;

public sealed class DocumentRevisionServiceTests
{
  private static (AppDbContext Db, DocumentRevisionService Service, Guid Id, string Tenant) Create()
  {
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
    var db = new AppDbContext(options);
    var tenant = $"tenant:{Guid.NewGuid():D}";
    var doc = new DocumentRecord
    {
      TenantId = tenant, OriginalFileName = "a.txt", ContentType = "text/plain",
      FileContent = Encoding.UTF8.GetBytes("original"), SizeBytes = 8
    };
    db.Documents.Add(doc);
    db.SaveChanges();
    return (db, new DocumentRevisionService(db), doc.Id, tenant);
  }

  [Fact]
  public async Task DraftOnlyPublishesAndEnqueuesAfterApproval()
  {
    var (db, service, id, tenant) = Create();
    var draft = await service.CreateAsync(id, tenant, Encoding.UTF8.GetBytes("changed"), null, 1, "bob", "fix", actorId: "bob-id");
    Assert.Equal("Draft", draft.Revision!.Status);
    Assert.Equal(0, (await db.Documents.FindAsync(id))!.CurrentRevisionNumber);
    Assert.Equal("original", Encoding.UTF8.GetString((await db.Documents.FindAsync(id))!.FileContent));
    Assert.Empty(db.OutboxMessages);
    Assert.Equal(RevisionOutcome.Conflict, (await service.TransitionAsync(id, 2, tenant, "approve", null, "reviewer", "reviewer")).Outcome);
    Assert.Equal("InReview", (await service.TransitionAsync(id, 2, tenant, "submit", null, "bob-id", "bob")).Revision!.Status);
    Assert.Empty(db.OutboxMessages);
    Assert.Equal("Approved", (await service.TransitionAsync(id, 2, tenant, "approve", null, "reviewer", "reviewer")).Revision!.Status);
    var doc = (await db.Documents.FindAsync(id))!;
    Assert.Equal(2, doc.CurrentRevisionNumber);
    Assert.Equal("changed", Encoding.UTF8.GetString(doc.FileContent));
    var message = await db.OutboxMessages.SingleAsync();
    using var payload = JsonDocument.Parse(message.Payload);
    Assert.Equal(2, payload.RootElement.GetProperty("revisionNumber").GetInt32());
    Assert.Equal(2, Assert.Single(OrchestrationEvent.Parse(Encoding.UTF8.GetBytes(message.Payload), null, null).Documents).RevisionNumber);
    Assert.Equal(new[] { "draft_created", "submit", "approve" }, (await db.RevisionActivities.OrderBy(a => a.CreatedAtUtc).Select(a => a.Action).ToListAsync()).ToArray());
  }

  [Fact]
  public async Task CompetingProposalsCannotBothBeApproved()
  {
    var (db, service, id, tenant) = Create();
    await service.CreateAsync(id, tenant, [1], null, 1, "a", "first");
    await service.CreateAsync(id, tenant, [2], null, 1, "b", "second");
    await service.TransitionAsync(id, 2, tenant, "submit", null, "a", "a");
    await service.TransitionAsync(id, 3, tenant, "submit", null, "b", "b");
    await service.TransitionAsync(id, 2, tenant, "approve", null, "r", "r");
    Assert.Equal(RevisionOutcome.Conflict, (await service.TransitionAsync(id, 3, tenant, "approve", null, "r", "r")).Outcome);
    Assert.Equal(RevisionOutcome.Invalid, (await service.TransitionAsync(id, 3, tenant, "reject", "", "r", "r")).Outcome);
    Assert.Equal("Rejected", (await service.TransitionAsync(id, 3, tenant, "reject", "obsolete", "r", "r")).Revision!.Status);
    Assert.Single(db.OutboxMessages);
  }

  [Fact]
  public async Task RestoreCreatesDraftWithoutChangingPublishedDocument()
  {
    var (db, service, id, tenant) = Create();
    await service.CreateAsync(id, tenant, [5], null, 1, "a", "change");
    await service.TransitionAsync(id, 2, tenant, "submit", null, "a", "a");
    await service.TransitionAsync(id, 2, tenant, "approve", null, "r", "r");
    var restored = await service.RestoreAsync(id, 1, tenant, 2, "a", "rollback");
    Assert.Equal("Draft", restored.Revision!.Status);
    Assert.Equal(3, restored.Revision.RevisionNumber);
    Assert.Equal(new byte[] { 5 }, (await db.Documents.FindAsync(id))!.FileContent);
    Assert.Single(db.OutboxMessages);
  }

  [Fact]
  public async Task ValidatesReasonsAndTenantIsolation()
  {
    var (_, service, id, tenant) = Create();
    var other = $"tenant:{Guid.NewGuid():D}";
    Assert.Equal(RevisionOutcome.Invalid, (await service.CreateAsync(id, tenant, [1], null, 1, "a", " ")).Outcome);
    Assert.Equal(RevisionOutcome.Invalid, (await service.CreateAsync(id, tenant, [], null, 1, "a", "reason")).Outcome);
    Assert.Null(await service.ListAsync(id, other));
    Assert.Null(await service.GetAsync(id, 1, other));
    Assert.Equal(RevisionOutcome.NotFound, (await service.CreateAsync(id, other, [1], null, 1, "a", "reason")).Outcome);
    Assert.Equal(RevisionOutcome.NotFound, (await service.TransitionAsync(id, 1, other, "approve", null, "a", "a")).Outcome);
  }

  [Fact]
  public async Task CommentsAreAnchoredAndTenantScoped()
  {
    var (db, service, id, tenant) = Create();
    await service.CreateAsync(id, tenant, Encoding.UTF8.GetBytes("hello world"), null, 1, "a", "edit");
    var collaboration = new RevisionCollaborationService(db, service);
    var root = await collaboration.AddCommentAsync(id, 2, tenant, null, 6, "world", "check", "a", "Alice", default);
    Assert.NotNull(root);
    await Assert.ThrowsAsync<ArgumentException>(() => collaboration.AddCommentAsync(id, 2, tenant, null, 0, "world", "wrong", "a", "Alice", default));
    var reply = await collaboration.AddCommentAsync(id, 2, tenant, root!.Id, 0, "", "done", "b", "Bob", default);
    Assert.Equal(root.Id, reply!.ParentId);
    Assert.True(await collaboration.ResolveAsync(id, 2, tenant, root.Id, true, "b", "Bob", default));
    await Assert.ThrowsAsync<ArgumentException>(() => collaboration.AddCommentAsync(id, 2, tenant, root.Id, 0, "", "late", "b", "Bob", default));
    Assert.Null(await collaboration.CommentsAsync(id, 2, $"tenant:{Guid.NewGuid():D}", default));
    Assert.Equal(2, (await collaboration.CommentsAsync(id, 2, tenant, default))!.Count);
  }

  [Fact]
  public async Task OriginalCanBeCommentedBeforeTheFirstDraft()
  {
    var (db, service, id, tenant) = Create();
    var collaboration = new RevisionCollaborationService(db, service);
    Assert.Empty((await collaboration.CommentsAsync(id, 1, tenant, default))!);
    var comment = await collaboration.AddCommentAsync(id, 1, tenant, null, 0, "original", "review", "a", "Alice", default);
    Assert.NotNull(comment);
    Assert.Single(await collaboration.CommentsAsync(id, 1, tenant, default));
    Assert.Equal("Draft", (await service.CreateAsync(id, tenant, [1], null, 1, "a", "edit")).Revision!.Status);
    Assert.Equal(2, await db.DocumentRevisions.CountAsync());
  }
}
