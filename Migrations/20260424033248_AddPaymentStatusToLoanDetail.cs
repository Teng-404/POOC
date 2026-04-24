using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POOC.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStatusToLoanDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "Members",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "Members",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "LoanDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidDate",
                table: "LoanDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_FirstName_LastName",
                table: "Members",
                columns: new[] { "FirstName", "LastName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Members_FirstName_LastName",
                table: "Members");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "LoanDetails");

            migrationBuilder.DropColumn(
                name: "PaidDate",
                table: "LoanDetails");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "Members",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "Members",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
