using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeChapterIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Annotations_Chapters_ChapterId",
                table: "Annotations");

            migrationBuilder.DropForeignKey(
                name: "FK_Bookmarks_Chapters_ChapterId",
                table: "Bookmarks");

            migrationBuilder.AlterColumn<Guid>(
                name: "ChapterId",
                table: "Bookmarks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ChapterId",
                table: "Annotations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Annotations_Chapters_ChapterId",
                table: "Annotations",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookmarks_Chapters_ChapterId",
                table: "Bookmarks",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Annotations_Chapters_ChapterId",
                table: "Annotations");

            migrationBuilder.DropForeignKey(
                name: "FK_Bookmarks_Chapters_ChapterId",
                table: "Bookmarks");

            migrationBuilder.AlterColumn<Guid>(
                name: "ChapterId",
                table: "Bookmarks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ChapterId",
                table: "Annotations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Annotations_Chapters_ChapterId",
                table: "Annotations",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookmarks_Chapters_ChapterId",
                table: "Bookmarks",
                column: "ChapterId",
                principalTable: "Chapters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
