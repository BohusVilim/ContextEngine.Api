using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContextEngine.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default is a valid empty JSON array ("[]"), not an empty string - Chunk.Embedding's
            // HasConversion (see ContextEngineDbContext) runs JsonSerializer.Deserialize<float[]> on
            // whatever is in this column, and "" is not valid JSON. This only matters for rows that
            // already existed before this migration ran; every row inserted afterwards goes through
            // the conversion itself and never touches this default.
            migrationBuilder.AddColumn<string>(
                name: "Embedding",
                table: "Chunks",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Chunks");
        }
    }
}
