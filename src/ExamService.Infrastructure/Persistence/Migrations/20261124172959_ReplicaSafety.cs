using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplicaSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AnomalyFlags_Dedupe",
                table: "AnomalyFlags",
                columns: new[] { "ExamId", "Kind", "AttemptId", "OtherAttemptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnomalyFlags_Dedupe",
                table: "AnomalyFlags");
        }
    }
}
