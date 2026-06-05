using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlueberryMart.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:order_status", "pending,confirmed,processing,ready,completed,cancelled")
                .Annotation("Npgsql:Enum:order_type", "pickup,delivery")
                .Annotation("Npgsql:Enum:payment_status", "initiated,completed,failed")
                .Annotation("Npgsql:Enum:user_role", "customer,shareholder")
                .OldAnnotation("Npgsql:Enum:order_status", "pending,confirmed,processing,ready,completed,cancelled")
                .OldAnnotation("Npgsql:Enum:order_type", "pickup,delivery")
                .OldAnnotation("Npgsql:Enum:user_role", "customer,shareholder");

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_uuid = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "initiated"),
                    provider_ref = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_payments_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payments_order_id",
                table: "payments",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_transaction_uuid",
                table: "payments",
                column: "transaction_uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:order_status", "pending,confirmed,processing,ready,completed,cancelled")
                .Annotation("Npgsql:Enum:order_type", "pickup,delivery")
                .Annotation("Npgsql:Enum:user_role", "customer,shareholder")
                .OldAnnotation("Npgsql:Enum:order_status", "pending,confirmed,processing,ready,completed,cancelled")
                .OldAnnotation("Npgsql:Enum:order_type", "pickup,delivery")
                .OldAnnotation("Npgsql:Enum:payment_status", "initiated,completed,failed")
                .OldAnnotation("Npgsql:Enum:user_role", "customer,shareholder");
        }
    }
}
