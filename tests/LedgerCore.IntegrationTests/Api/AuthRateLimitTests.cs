using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Api;

/// <summary>
/// Login and register are open to anyone, so one client address may only try so fast
/// (ADR 0013). These tests host the API with a limit of 5 requests per window.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuthRateLimitTests(SqlServerFixture fixture)
{
    private const int Limit = 5;

    [Fact]
    public async Task Auth_requests_over_the_limit_get_429_with_retry_after_and_problem_details()
    {
        await using var api = new ApiFactory(fixture.ConnectionString, authPermitLimit: Limit);
        var client = api.CreateClientWithIdempotencyKeys();

        // Wrong passwords count as much as right ones: that is the point.
        for (var i = 0; i < Limit; i++)
        {
            var attempt = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong-password" });
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var limited = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong-password" });

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        limited.Headers.RetryAfter.Should().NotBeNull();
        limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeGreaterThan(0);

        var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("status").GetInt32().Should().Be(429);
        problem.GetProperty("code").GetString().Should().Be("rate_limited");
        problem.GetProperty("correlationId").GetString().Should().Be(limited.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Register_shares_the_limit_with_login()
    {
        await using var api = new ApiFactory(fixture.ConnectionString, authPermitLimit: Limit);
        var client = api.CreateClientWithIdempotencyKeys();

        for (var i = 0; i < Limit; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong-password" });
        }

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email = $"{Guid.NewGuid():N}@test.local", password = "long-enough-pass" });

        register.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Other_endpoints_are_not_limited_when_the_auth_limit_is_used_up()
    {
        await using var api = new ApiFactory(fixture.ConnectionString, authPermitLimit: Limit);

        // Registering and logging in uses two of the five.
        var user = await api.CreateUserClientAsync();
        for (var i = 0; i < Limit - 2; i++)
        {
            await user.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong-password" });
        }

        (await user.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong-password" }))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await user.GetAsync("/api/accounts")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_client_under_the_limit_is_never_turned_away()
    {
        await using var api = new ApiFactory(fixture.ConnectionString, authPermitLimit: Limit);
        var client = api.CreateClientWithIdempotencyKeys();

        for (var i = 0; i < Limit; i++)
        {
            (await client.PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = ApiFactory.Password }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
