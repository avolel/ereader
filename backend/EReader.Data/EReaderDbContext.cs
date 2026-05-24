using EReader.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EReader.Data;

public class EReaderDbContext(DbContextOptions<EReaderDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<Annotation> Annotations => Set<Annotation>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<ReadingSetting> ReadingSettings => Set<ReadingSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Book>(book =>
        {
            book.HasIndex(b => b.FileHash);
            book.HasIndex(b => new { b.UserId, b.FileHash }).IsUnique();
        });

        modelBuilder.Entity<Chapter>(chapter =>
        {
            chapter.HasIndex(c => new { c.BookId, c.SpineOrder }).IsUnique();

            chapter.HasOne(c => c.Book)
                .WithMany(b => b.Chapters)
                .HasForeignKey(c => c.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Annotation>(annotation =>
        {
            annotation.HasOne(a => a.Book)
                .WithMany(b => b.Annotations)
                .HasForeignKey(a => a.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict here avoids the multi-path cascade that PostgreSQL rejects:
            // Book→Chapter (Cascade) + Annotation→Chapter (Cascade) would be two
            // deletion paths to the same row. The Book cascade already handles cleanup.
            annotation.HasOne(a => a.Chapter)
                .WithMany(c => c.Annotations)
                .HasForeignKey(a => a.ChapterId)
                .OnDelete(DeleteBehavior.Restrict);

            annotation.HasOne(a => a.User)
                .WithMany(u => u.Annotations)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bookmark>(bookmark =>
        {
            bookmark.HasOne(bm => bm.Book)
                .WithMany(b => b.Bookmarks)
                .HasForeignKey(bm => bm.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            bookmark.HasOne(bm => bm.Chapter)
                .WithMany(c => c.Bookmarks)
                .HasForeignKey(bm => bm.ChapterId)
                .OnDelete(DeleteBehavior.Restrict);

            bookmark.HasOne(bm => bm.User)
                .WithMany(u => u.Bookmarks)
                .HasForeignKey(bm => bm.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingSetting>(rs =>
        {
            // Unique per (UserId, BookId): one global default (BookId null) and
            // at most one per-book override per user.
            rs.HasIndex(r => new { r.UserId, r.BookId }).IsUnique();

            rs.HasOne(r => r.User)
                .WithMany(u => u.ReadingSettings)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            rs.HasOne(r => r.Book)
                .WithMany(b => b.ReadingSettings)
                .HasForeignKey(r => r.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
