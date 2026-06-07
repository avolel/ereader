using System.Text;
using EReader.Data.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace EReader.Api.Auth;

public static class JwtBearerSetup
{
    public static WebApplicationBuilder AddEreaderAuth(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Key), "Jwt:Key is required (set JWT__KEY).")
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32,
                "Jwt:Key must be at least 32 bytes (UTF-8).")
            .ValidateOnStart();

        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt section missing from configuration.");

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // Native WebView/<Image> subresource requests can't send an
                // Authorization header, so for the two media GET routes (cover +
                // in-chapter assets) we also accept the token via ?access_token=.
                // MediaQueryToken scopes this to those paths; every other route
                // still requires the header. Per-user ownership checks in the
                // service layer are unchanged — this only adds a token transport.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = MediaQueryToken.ResolveQueryToken(
                            context.Request.Method,
                            context.Request.Path,
                            context.Request.Query[MediaQueryToken.QueryKey]);
                        if (token is not null)
                        {
                            context.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization();

        return builder;
    }
}
