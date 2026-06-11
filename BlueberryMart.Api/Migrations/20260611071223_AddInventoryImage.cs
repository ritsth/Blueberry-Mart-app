using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueberryMart.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "inventory",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_url",
                table: "inventory");
        }
    }
}
