using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EReader.Core.Auth;
using EReader.Core.Interfaces;
using EReader.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EReader.Data.Auth;

public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    public const string FamilyClaim = "fid";

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenIssuer(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        var keyBytes = Encoding.UTF8.GetBytes(_options.Key);
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    public IssuedAccessToken IssueAccessToken(User user, Guid familyId)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(FamilyClaim, familyId.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);

        var handler = new JwtSecurityTokenHandler();
        // The default handler maps `sub` → ClaimTypes.NameIdentifier on the
        // *reading* side, which is what HttpContextCurrentUserService relies on.
        // No special config needed here on the issuing side.
        return new IssuedAccessToken(handler.WriteToken(token), expires);
    }
}
