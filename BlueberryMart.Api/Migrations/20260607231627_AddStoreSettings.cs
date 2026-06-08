using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueberryMart.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_fee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    membership_monthly_fee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    member_discount_rate = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    maintenance_mode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    maintenance_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_settings");
        }
    }
}
