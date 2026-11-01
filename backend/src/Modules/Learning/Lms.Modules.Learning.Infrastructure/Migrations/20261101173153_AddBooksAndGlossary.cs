using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Learning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBooksAndGlossary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StructureJson",
                schema: "learning",
                table: "Activities",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GlossaryEntries",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DefinitionAr = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DefinitionEn = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    TermAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TermEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryEntries_AuthorId",
                schema: "learning",
                table: "GlossaryEntries",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryEntries_CourseId",
                schema: "learning",
                table: "GlossaryEntries",
                column: "CourseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlossaryEntries",
                schema: "learning");

            migrationBuilder.DropColumn(
                name: "StructureJson",
                schema: "learning",
                table: "Activities");
        }
    }
}
