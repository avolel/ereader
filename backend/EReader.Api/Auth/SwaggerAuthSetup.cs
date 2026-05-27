using Microsoft.OpenApi;

namespace EReader.Api.Auth;

public static class SwaggerAuthSetup
{
    public static IServiceCollection AddSwaggerWithAuth(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste JWT access token (no 'Bearer ' prefix).",
            });

            // Swashbuckle 10 / Microsoft.OpenApi 2.x: AddSecurityRequirement takes
            // a Func<OpenApiDocument, OpenApiSecurityRequirement> so the requirement
            // can reference the security scheme by id within the document.
            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("Bearer", doc, null),
                    new List<string>()
                },
            });
        });

        return services;
    }
}
