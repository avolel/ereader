using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct);

    Task AddAsync(User user, CancellationToken ct);

    Task UpdateAsync(User user, CancellationToken ct);
}
