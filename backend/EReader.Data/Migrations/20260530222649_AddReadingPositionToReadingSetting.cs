using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingPositionToReadingSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastChapterId",
                table: "ReadingSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadAt",
                table: "ReadingSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastScrollOffset",
                table: "ReadingSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastChapterId",
                table: "ReadingSettings");

            migrationBuilder.DropColumn(
                name: "LastReadAt",
                table: "ReadingSettings");

            migrationBuilder.DropColumn(
                name: "LastScrollOffset",
                table: "ReadingSettings");
        }
    }
}
