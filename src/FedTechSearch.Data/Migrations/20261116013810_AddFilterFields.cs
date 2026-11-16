using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FedTechSearch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFilterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppointmentType",
                table: "jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MayCloseEarly",
                table: "jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PromotionPotential",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleType",
                table: "jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TravelRequirement",
                table: "jobs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_Department",
                table: "jobs",
                column: "Department");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_HiringPaths",
                table: "jobs",
                column: "HiringPaths")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_PromotionPotential",
                table: "jobs",
                column: "PromotionPotential");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_jobs_Department",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_jobs_HiringPaths",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_jobs_PromotionPotential",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "AppointmentType",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "MayCloseEarly",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "PromotionPotential",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "ScheduleType",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "TravelRequirement",
                table: "jobs");
        }
    }
}
