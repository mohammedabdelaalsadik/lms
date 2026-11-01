using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Learning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveAttendance",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LiveClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraineeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveAttendance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiveClasses",
                schema: "learning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CohortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    MeetingId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TitleAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveClasses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveAttendance_LiveClassId_TraineeId",
                schema: "learning",
                table: "LiveAttendance",
                columns: new[] { "LiveClassId", "TraineeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiveAttendance_TraineeId",
                schema: "learning",
                table: "LiveAttendance",
                column: "TraineeId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveClasses_CohortId",
                schema: "learning",
                table: "LiveClasses",
                column: "CohortId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveAttendance",
                schema: "learning");

            migrationBuilder.DropTable(
                name: "LiveClasses",
                schema: "learning");
        }
    }
}
