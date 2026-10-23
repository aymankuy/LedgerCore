using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Api;

/// <summary>Cross-cutting HTTP behaviour: correlation ids, error shape, Swagger.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PlatformApiTests(SqlServerFixture fixture)
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [Fact]
    public async Task A_safe_caller_correlation_id_is_echoed_back()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        request.Headers.Add(CorrelationHeader, "client-req-42");

        var response = await fixture.Api.CreateClient().SendAsync(request);

        response.Headers.GetValues(CorrelationHeader).Should().ContainSingle().Which.Should().Be("client-req-42");
    }

    [Fact]
    public async Task An_unsafe_correlation_id_is_replaced()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        request.Headers.TryAddWithoutValidation(CorrelationHeader, "bad id\" with quotes");

        var response = await fixture.Api.CreateClient().SendAsync(request);

        response.Headers.GetValues(CorrelationHeader).Single().Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Error_responses_are_problem_details_with_the_correlation_id_and_no_internals()
    {
        var response = await fixture.Api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("correlationId").GetString().Should().Be(response.Headers.GetValues(CorrelationHeader).Single());
        body.Should().NotContain("   at ", "no stack trace may reach the client");
    }

    [Fact]
    public async Task Swagger_document_describes_the_api_and_its_bearer_scheme()
    {
        var document = await fixture.Api.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        document.GetProperty("paths").TryGetProperty("/api/transfers", out _).Should().BeTrue();
        document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
    }
}
