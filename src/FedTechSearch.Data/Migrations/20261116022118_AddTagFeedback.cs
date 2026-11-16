using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FedTechSearch.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tag_feedback",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Field = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SuggestedValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TaxonomyVersion = table.Column<int>(type: "integer", nullable: false),
                    ClientHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tag_feedback_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tag_feedback_CreatedAt",
                table: "tag_feedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_tag_feedback_Field_CurrentValue",
                table: "tag_feedback",
                columns: new[] { "Field", "CurrentValue" });

            migrationBuilder.CreateIndex(
                name: "IX_tag_feedback_JobId",
                table: "tag_feedback",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tag_feedback");
        }
    }
}
