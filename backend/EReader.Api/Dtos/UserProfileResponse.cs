using EReader.Core.Models;

namespace EReader.Api.Dtos;

public sealed record UserProfileResponse(
    Guid Id,
    string Username,
    DateTime CreatedAt,
    DateTime? LastLoginAt)
{
    public static UserProfileResponse From(User user) =>
        new(user.Id, user.Username, user.CreatedAt, user.LastLoginAt);
}
