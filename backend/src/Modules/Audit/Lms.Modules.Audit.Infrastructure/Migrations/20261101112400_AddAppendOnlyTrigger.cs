using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Modules.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            // The log cannot be edited, even by the application's own database account.
            migrationBuilder.Sql("""
                CREATE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    THROW 51000, 'The audit log is append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("DROP TRIGGER audit.TR_AuditEntries_AppendOnly");
        }
    }
}
