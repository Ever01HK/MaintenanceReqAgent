using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintenanceAgent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "MaintenanceApprovals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "MaintenanceApprovals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceApprovals_IdempotencyKey",
                table: "MaintenanceApprovals",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaintenanceApprovals_IdempotencyKey",
                table: "MaintenanceApprovals");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "MaintenanceApprovals");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "MaintenanceApprovals");
        }
    }
}
