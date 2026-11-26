using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FedTechSearch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_stats",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Dimension = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActiveCount = table.Column<int>(type: "integer", nullable: false),
                    NewCount = table.Column<int>(type: "integer", nullable: false),
                    RemoteCount = table.Column<int>(type: "integer", nullable: false),
                    TeleworkCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_stats", x => new { x.Date, x.Dimension, x.Value });
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_stats_Dimension_Date",
                table: "daily_stats",
                columns: new[] { "Dimension", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_stats");
        }
    }
}
