using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueberryMart.Api.Migrations
{
    /// <inheritdoc />
    public partial class MembershipDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // New columns
            migrationBuilder.AddColumn<DateTime>(
                name: "member_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "membership_cancelled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve existing members: give them a one-month period from now
            migrationBuilder.Sql(
                "UPDATE users SET member_until = NOW() + INTERVAL '1 month' WHERE is_member = true;");

            // The old flag is now derived from member_until
            migrationBuilder.DropColumn(
                name: "is_member",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE users SET is_member = (member_until IS NOT NULL AND member_until > NOW());");

            migrationBuilder.DropColumn(
                name: "membership_cancelled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "member_until",
                table: "users");
        }
    }
}
