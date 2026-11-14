using System.Net.Http.Headers;
using System.Net.Http.Json;
using LedgerCore.Application.Abstractions.Authentication;
using LedgerCore.Application.Abstractions.Idempotency;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LedgerCore.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in memory, configured the way docker compose configures it: startup
/// migrations and seeding on, settings from configuration rather than appsettings files.
/// </summary>
public sealed class ApiFactory(string connectionString, string? redisConnectionString = null) : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "integration-pass";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ledger", connectionString);

        // No Redis unless a test asks for it: then balance reads are cached exactly as in compose.
        if (redisConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Redis", redisConnectionString);
        }

        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Database:AdminEmail", AdminEmail);
        builder.UseSetting("Database:AdminPassword", Password);
    }

    /// <summary>Registers a new user and returns a client that sends their token.</summary>
    public async Task<HttpClient> CreateUserClientAsync()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var client = CreateClientWithIdempotencyKeys();
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        return await AuthenticateAsync(client, email);
    }

    public Task<HttpClient> CreateAdminClientAsync() => AuthenticateAsync(CreateClientWithIdempotencyKeys(), AdminEmail);

    /// <summary>
    /// A client that adds a fresh Idempotency-Key to every POST that has none, like a well-behaved
    /// client would. Tests that check idempotency set the header themselves.
    /// </summary>
    public HttpClient CreateClientWithIdempotencyKeys() => CreateDefaultClient(new IdempotencyKeyHandler());

    private static async Task<HttpClient> AuthenticateAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AccessToken>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }
}

internal sealed class IdempotencyKeyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !request.Headers.Contains(IIdempotencyContext.HeaderName))
        {
            request.Headers.Add(IIdempotencyContext.HeaderName, Guid.NewGuid().ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
