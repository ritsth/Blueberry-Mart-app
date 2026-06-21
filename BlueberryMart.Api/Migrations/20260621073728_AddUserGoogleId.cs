using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueberryMart.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGoogleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "google_id",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_google_id",
                table: "users",
                column: "google_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_google_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "google_id",
                table: "users");
        }
    }
}
