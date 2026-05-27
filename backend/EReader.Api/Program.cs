using DotNetEnv;
using EReader.Api.Auth;
using EReader.Api.Middleware;
using EReader.Core.Interfaces;
using EReader.Core.Services;
using EReader.Data;
using EReader.Data.Auth;
using EReader.Data.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

// In Development, load .env from the project directory so DATABASE_URL is available
// to the configuration system. In production, DATABASE_URL must be set as a real
// environment variable — no .env file should exist there.
if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
{
    Env.TraversePath().Load();
}

var builder = WebApplication.CreateBuilder(args);

// Every endpoint is authenticated by default; opt-out with [AllowAnonymous].
builder.Services.AddControllers(opts =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    opts.Filters.Add(new AuthorizeFilter(policy));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithAuth();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<EReaderDbContext>(options =>
    options.UseNpgsql(connectionString));

// Redis (refresh token store). Singleton multiplexer is the StackExchange.Redis
// recommended pattern.
builder.Services
    .AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
        "Redis:ConnectionString is required (set REDIS__CONNECTIONSTRING).")
    .ValidateOnStart();

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConn = builder.Configuration["Redis:ConnectionString"]
        ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
    return ConnectionMultiplexer.Connect(redisConn);
});

// Auth: options + JwtBearer + Authorization wired in one extension.
builder.AddEreaderAuth();

// Auth services + helpers.
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();
// Singleton: the store has no per-request state — its only dependencies are the
// singleton IConnectionMultiplexer and IOptions<RedisOptions>.
builder.Services.AddSingleton<IRefreshTokenStore, RedisRefreshTokenStore>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // Auto-apply pending EF migrations on startup so pulling a branch with a new
    // migration doesn't 500 the first request. Dev-only — production migrations
    // must be gated through a deliberate deployment step.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<EReaderDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription()
    .AllowAnonymous();

app.Run();
