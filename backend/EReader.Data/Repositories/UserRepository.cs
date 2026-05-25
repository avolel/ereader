using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EReader.Data.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly EReaderDbContext _db;

    public UserRepository(EReaderDbContext db)
    {
        _db = db;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        var normalized = username.ToLowerInvariant();
        return _db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == normalized, ct);
    }

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct)
    {
        var normalized = username.ToLowerInvariant();
        return _db.Users.AnyAsync(u => u.Username.ToLower() == normalized, ct);
    }

    public async Task AddAsync(User user, CancellationToken ct)
    {
        await _db.Users.AddAsync(user, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task UpdateAsync(User user, CancellationToken ct)
    {
        _db.Users.Update(user);
        return _db.SaveChangesAsync(ct);
    }
}
