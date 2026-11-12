using System.Reflection;
using Microsoft.OpenApi.Models;

namespace LedgerCore.Api.OpenApi;

internal static class SwaggerSetup
{
    public static IServiceCollection AddLedgerSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "LedgerCore API",
                Version = "v1",
                Description = "Double-entry wallet and payment ledger. Log in via /api/auth/login, then use Authorize.",
            });

            // Lets Swagger UI send the token: paste it once into the Authorize dialog.
            var bearer = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            options.AddSecurityDefinition("Bearer", bearer);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = [] });

            options.OperationFilter<IdempotencyKeyOperationFilter>();

            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            options.IncludeXmlComments(xmlFile);
        });

        return services;
    }

    /// <summary>Served in every environment: the deployed demo is meant to be explored.</summary>
    public static WebApplication UseLedgerSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.DocumentTitle = "LedgerCore API");
        return app;
    }
}
