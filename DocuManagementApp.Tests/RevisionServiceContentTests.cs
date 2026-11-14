using DocuManagementApp.Controllers;
using DocuManagementApp.Data;
using DocuManagementApp.Models;
using DocuManagementApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DocuManagementApp.Tests;

public sealed class RevisionServiceContentTests
{
    [Fact]
    public async Task ServiceContentRequiresKeyAndTenantAndReturnsRequestedSnapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new AppDbContext(options);
        var tenant = $"tenant:{Guid.NewGuid():D}";
        var document = new DocumentRecord { TenantId = tenant, FileContent = [1], SizeBytes = 1 };
        db.Documents.Add(document);
        await db.SaveChangesAsync();
        var service = new DocumentRevisionService(db);
        await service.CreateAsync(document.Id, tenant, [2], null, 1, "editor", null);
        var controller = new DocumentRevisionsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceDownload:ApiKey"] = "test-service-key"
        }).Build();

        Assert.IsType<UnauthorizedResult>(await controller.ServiceContent(document.Id, 1, config, CancellationToken.None));
        controller.Request.Headers["X-Service-Key"] = "test-service-key";
        Assert.IsType<BadRequestObjectResult>(await controller.ServiceContent(document.Id, 1, config, CancellationToken.None));
        controller.Request.Headers["X-Tenant-Id"] = $"tenant:{Guid.NewGuid():D}";
        Assert.IsType<NotFoundResult>(await controller.ServiceContent(document.Id, 1, config, CancellationToken.None));
        controller.Request.Headers["X-Tenant-Id"] = tenant;
        var result = Assert.IsType<FileContentResult>(await controller.ServiceContent(document.Id, 1, config, CancellationToken.None));
        Assert.Equal(new byte[] { 1 }, result.FileContents);
    }
}
