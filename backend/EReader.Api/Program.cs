using DotNetEnv;
using EReader.Api.Auth;
using EReader.Api.Middleware;
using EReader.Core.Interfaces;
using EReader.Core.Services;
using EReader.Data;
using EReader.Data.Auth;
using EReader.Data.Parsing;
using EReader.Data.Repositories;
using EReader.Data.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
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

// --- MinIO object storage ---
var minioEndpoint  = builder.Configuration["MINIO_ENDPOINT"] ?? "localhost:9000";
var minioAccessKey = builder.Configuration["MINIO_USER"]     ?? throw new InvalidOperationException("MINIO_USER is required.");
var minioSecretKey = builder.Configuration["MINIO_PASSWORD"] ?? throw new InvalidOperationException("MINIO_PASSWORD is required.");
var minioBucket    = builder.Configuration["MINIO_BUCKET"]   ?? "ereader-media";

builder.Services.Configure<MinioOptions>(o =>
{
    o.Endpoint = minioEndpoint;
    o.AccessKey = minioAccessKey;
    o.SecretKey = minioSecretKey;
    o.Bucket = minioBucket;
    o.UseSSL = false;
});

builder.Services.AddSingleton<IMinioClient>(_ =>
    new MinioClient()
        .WithEndpoint(minioEndpoint)
        .WithCredentials(minioAccessKey, minioSecretKey)
        .WithSSL(false)
        .Build());

builder.Services.AddSingleton<IBookFileStore, MinioBookFileStore>();
builder.Services.AddTransient<IEpubParser, EpubParserAdapter>();
builder.Services.AddTransient<IEpubAssetReader, ZipEpubAssetReader>();
builder.Services.AddScoped<IBookRepository, BookRepository>();
builder.Services.AddScoped<IBookService, BookService>();
builder.Services.AddScoped<IBookIngestionService, BookIngestionService>();
builder.Services.AddScoped<ISearchRepository, SearchRepository>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<IReadingSettingsRepository, ReadingSettingsRepository>();
builder.Services.AddScoped<IReadingSettingsService, ReadingSettingsService>();

// Dev-only CORS so the Expo web dev server (localhost:8081 by default) can hit the API.
// Production CORS is intentionally not configured here — set per-environment when we ship.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DevFrontend", policy => policy
            .WithOrigins(
                "http://localhost:8081",
                "http://localhost:19006",
                "http://127.0.0.1:8081",
                "http://127.0.0.1:19006")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });
}

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // CORS must run before auth so preflight OPTIONS requests aren't rejected.
    app.UseCors("DevFrontend");

    // Auto-apply pending EF migrations on startup so pulling a branch with a new
    // migration doesn't 500 the first request. Dev-only — production migrations
    // must be gated through a deliberate deployment step.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<EReaderDbContext>();
        await db.Database.MigrateAsync();
        
        var minio = scope.ServiceProvider.GetRequiredService<IMinioClient>();
        var bucket = scope.ServiceProvider.GetRequiredService<IOptions<MinioOptions>>().Value.Bucket;
        if (!await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)))
        {
            await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
        }
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
