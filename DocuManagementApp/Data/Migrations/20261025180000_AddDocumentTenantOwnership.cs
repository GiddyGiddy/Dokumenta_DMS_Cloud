using DocuManagementApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuManagementApp.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261025180000_AddDocumentTenantOwnership")]
public sealed class AddDocumentTenantOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TenantId",
            table: "documents",
            type: "character varying(96)",
            maxLength: 96,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_documents_TenantId",
            table: "documents",
            column: "TenantId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_documents_TenantId",
            table: "documents");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "documents");
    }
}
