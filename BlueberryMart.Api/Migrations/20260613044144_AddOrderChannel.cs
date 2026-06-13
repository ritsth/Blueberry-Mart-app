using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueberryMart.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "orders",
                type: "text",
                nullable: false,
                defaultValue: "online");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "channel",
                table: "orders");
        }
    }
}
