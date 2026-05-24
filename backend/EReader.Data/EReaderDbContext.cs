using EReader.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EReader.Data;

public class EReaderDbContext(DbContextOptions<EReaderDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
}
