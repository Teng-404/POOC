using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POOC.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Members",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedDate",
                table: "Members",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Members",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedDate",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedDate",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedDate",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuarantorAddress",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuarantorName",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuarantorPhone",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Loans",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Loans",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "PaidAmount",
                table: "LoanDetails",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "PenaltyPaid",
                table: "LoanDetails",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavingsInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MemberId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    PrincipalSnapshot = table.Column<decimal>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    InterestAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsInterests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedDate = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "SavingsInterests");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "DeletedDate",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ApprovedDate",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ClosedDate",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "DeletedDate",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "GuarantorAddress",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "GuarantorName",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "GuarantorPhone",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "LoanDetails");

            migrationBuilder.DropColumn(
                name: "PenaltyPaid",
                table: "LoanDetails");
        }
    }
}
