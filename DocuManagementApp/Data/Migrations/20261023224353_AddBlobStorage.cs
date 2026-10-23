using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuManagementApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlobStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlobName",
                table: "document_revisions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "BlobName",
                table: "document_revisions");
        }
    }
}
