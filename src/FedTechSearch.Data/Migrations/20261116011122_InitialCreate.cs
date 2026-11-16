using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FedTechSearch.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AnnouncementNumber = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    ApplyUrl = table.Column<string>(type: "text", nullable: true),
                    OfficialTitle = table.Column<string>(type: "text", nullable: false),
                    Agency = table.Column<string>(type: "text", nullable: false),
                    Department = table.Column<string>(type: "text", nullable: false),
                    SubAgency = table.Column<string>(type: "text", nullable: true),
                    LocationDisplay = table.Column<string>(type: "text", nullable: false),
                    Locations = table.Column<List<string>>(type: "text[]", nullable: false),
                    States = table.Column<List<string>>(type: "text[]", nullable: false),
                    Series = table.Column<List<string>>(type: "text[]", nullable: false),
                    PayPlan = table.Column<string>(type: "text", nullable: true),
                    LowGrade = table.Column<int>(type: "integer", nullable: true),
                    HighGrade = table.Column<int>(type: "integer", nullable: true),
                    SalaryMin = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    SalaryMax = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    SalaryInterval = table.Column<string>(type: "text", nullable: true),
                    OpenDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CloseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Schedule = table.Column<string>(type: "text", nullable: true),
                    OfferingType = table.Column<string>(type: "text", nullable: true),
                    HiringPaths = table.Column<List<string>>(type: "text[]", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Duties = table.Column<List<string>>(type: "text[]", nullable: false),
                    Qualifications = table.Column<string>(type: "text", nullable: false),
                    WorkingTitle = table.Column<string>(type: "text", nullable: false),
                    WorkingTitleSource = table.Column<string>(type: "text", nullable: false),
                    RoleFamily = table.Column<string>(type: "text", nullable: false),
                    RoleConfidence = table.Column<double>(type: "double precision", nullable: false),
                    SpecialtyCodes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Specialties = table.Column<List<string>>(type: "text[]", nullable: false),
                    Tech = table.Column<List<string>>(type: "text[]", nullable: false),
                    Seniority = table.Column<List<string>>(type: "text[]", nullable: false),
                    Supervisory = table.Column<bool>(type: "boolean", nullable: false),
                    WorkArrangement = table.Column<string>(type: "text", nullable: false),
                    Clearance = table.Column<string>(type: "text", nullable: false),
                    DirectHire = table.Column<bool>(type: "boolean", nullable: false),
                    OpenToPublic = table.Column<bool>(type: "boolean", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    EnrichmentVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_CloseDate",
                table: "jobs",
                column: "CloseDate");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_IsActive",
                table: "jobs",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_OpenDate",
                table: "jobs",
                column: "OpenDate");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_RoleFamily",
                table: "jobs",
                column: "RoleFamily");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_Seniority",
                table: "jobs",
                column: "Seniority")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_SpecialtyCodes",
                table: "jobs",
                column: "SpecialtyCodes")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_States",
                table: "jobs",
                column: "States")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_Tech",
                table: "jobs",
                column: "Tech")
                .Annotation("Npgsql:IndexMethod", "gin");

            // Expression index for full-text search; EF can't model it, so it lives here as SQL.
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS ix_jobs_search ON jobs USING GIN (to_tsvector('english', "SearchText"));""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_jobs_search;");

            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
