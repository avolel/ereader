using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookPublishedYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublishedYear",
                table: "Books",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedYear",
                table: "Books");
        }
    }
}
