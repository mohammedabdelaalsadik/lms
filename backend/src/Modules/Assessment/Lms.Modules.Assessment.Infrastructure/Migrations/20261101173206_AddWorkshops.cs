using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Assessment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkshops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkshopReviews",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkshopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workshops",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CohortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReviewsPerPerson = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    InstructionsAr = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    InstructionsEn = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workshops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkshopSubmissions",
                schema: "assessment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkshopId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraineeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopSubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkshopReviews_ReviewerId",
                schema: "assessment",
                table: "WorkshopReviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkshopReviews_SubmissionId_ReviewerId",
                schema: "assessment",
                table: "WorkshopReviews",
                columns: new[] { "SubmissionId", "ReviewerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkshopReviews_WorkshopId",
                schema: "assessment",
                table: "WorkshopReviews",
                column: "WorkshopId");

            migrationBuilder.CreateIndex(
                name: "IX_Workshops_CohortId",
                schema: "assessment",
                table: "Workshops",
                column: "CohortId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkshopSubmissions_WorkshopId_TraineeId",
                schema: "assessment",
                table: "WorkshopSubmissions",
                columns: new[] { "WorkshopId", "TraineeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkshopReviews",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "Workshops",
                schema: "assessment");

            migrationBuilder.DropTable(
                name: "WorkshopSubmissions",
                schema: "assessment");
        }
    }
}
