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

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Username).IsRequired().HasMaxLength(32);
            user.Property(u => u.PasswordHash).IsRequired();

            // Uniqueness is enforced case-insensitively. EF generates a normal
            // unique index here; the migration is hand-edited to use LOWER(username)
            // so 'Alice' and 'alice' can't coexist without depending on the
            // citext extension.
            user.HasIndex(u => u.Username).IsUnique().HasDatabaseName("IX_Users_Username_Lower");
        });

        modelBuilder.Entity<Book>(book =>
        {
            book.HasIndex(b => b.FileHash);
            book.HasIndex(b => new { b.UserId, b.FileHash }).IsUnique();

            // Restrict (not Cascade) so deleting a user doesn't silently wipe their
            // library. Forces the service layer to make an explicit choice: hard-delete
            // the library first, or (future) soft-delete/anonymize the user and leave
            // owned rows intact. Keeps the soft-delete door open without DB rework.
            book.HasOne(b => b.User)
                .WithMany(u => u.Books)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.Restrict);
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

            // SetNull (not Cascade) so a chapter can be regenerated on EPUB re-import
            // without dropping the user's annotations. TextAnchor is the durable
            // identity; ChapterId is re-resolved at read time when null.
            annotation.HasOne(a => a.Chapter)
                .WithMany(c => c.Annotations)
                .HasForeignKey(a => a.ChapterId)
                .OnDelete(DeleteBehavior.SetNull);

            // Restrict on User: same reasoning as Book — preserve the option of
            // soft-delete/anonymization. Book → Annotations stays Cascade so book
            // deletion still cleans up normally.
            annotation.HasOne(a => a.User)
                .WithMany(u => u.Annotations)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);
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
                .OnDelete(DeleteBehavior.SetNull);

            bookmark.HasOne(bm => bm.User)
                .WithMany(u => u.Bookmarks)
                .HasForeignKey(bm => bm.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReadingSetting>(rs =>
        {
            // Unique per (UserId, BookId): one global default (BookId null) and
            // at most one per-book override per user.
            rs.HasIndex(r => new { r.UserId, r.BookId }).IsUnique();

            rs.HasOne(r => r.User)
                .WithMany(u => u.ReadingSettings)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            rs.HasOne(r => r.Book)
                .WithMany(b => b.ReadingSettings)
                .HasForeignKey(r => r.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
