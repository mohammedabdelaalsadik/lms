using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Learning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormJson",
                schema: "learning",
                table: "Activities",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormResponses",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CohortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraineeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Answers = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormSubmissions",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CohortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraineeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormResponses_CohortId_ActivityId",
                schema: "learning",
                table: "FormResponses",
                columns: new[] { "CohortId", "ActivityId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormResponses_TraineeId",
                schema: "learning",
                table: "FormResponses",
                column: "TraineeId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_ActivityId_CohortId_TraineeId",
                schema: "learning",
                table: "FormSubmissions",
                columns: new[] { "ActivityId", "CohortId", "TraineeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_CohortId_TraineeId",
                schema: "learning",
                table: "FormSubmissions",
                columns: new[] { "CohortId", "TraineeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormResponses",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "FormSubmissions",
                schema: "learning");

            migrationBuilder.DropColumn(
                name: "FormJson",
                schema: "learning",
                table: "Activities");
        }
    }
}
