namespace EReader.Api.Dtos;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    bool RevokeOtherSessions);
