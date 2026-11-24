using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tier2Anomalies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuestionCount",
                table: "Exams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresKiosk",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AttestationJson",
                table: "Attempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientMode",
                table: "Attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RandomizationSeed",
                table: "Attempts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnomalyFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    OtherAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<decimal>(type: "numeric", nullable: false),
                    DetailJson = table.Column<string>(type: "text", nullable: true),
                    Reviewed = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnomalyFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttemptContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    DeviceFingerprint = table.Column<string>(type: "text", nullable: true),
                    CountryCode = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptContexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttemptQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ShuffledOptionsJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptQuestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFlags_ExamId_Kind",
                table: "AnomalyFlags",
                columns: new[] { "ExamId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptContexts_AttemptId_CapturedAt",
                table: "AttemptContexts",
                columns: new[] { "AttemptId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptContexts_DeviceFingerprint",
                table: "AttemptContexts",
                column: "DeviceFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_AttemptContexts_IpAddress",
                table: "AttemptContexts",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_AttemptQuestions_AttemptId_Order",
                table: "AttemptQuestions",
                columns: new[] { "AttemptId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnomalyFlags");

            migrationBuilder.DropTable(
                name: "AttemptContexts");

            migrationBuilder.DropTable(
                name: "AttemptQuestions");

            migrationBuilder.DropColumn(
                name: "QuestionCount",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RequiresKiosk",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "AttestationJson",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "ClientMode",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "RandomizationSeed",
                table: "Attempts");
        }
    }
}
