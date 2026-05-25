using EReader.Core.Auth;
using EReader.Core.Models;

namespace EReader.Core.Interfaces;

public interface IJwtTokenIssuer
{
    IssuedAccessToken IssueAccessToken(User user, Guid familyId);
}
