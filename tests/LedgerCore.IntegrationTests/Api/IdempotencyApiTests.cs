using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LedgerCore.Api.Controllers;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Api;

/// <summary>Spec rule 6 over HTTP: the Idempotency-Key header on write endpoints.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class IdempotencyApiTests(SqlServerFixture fixture)
{
    private const string Header = "Idempotency-Key";

    private ApiFactory Api => fixture.Api;

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_first_response_and_moves_money_once()
    {
        var alice = await Api.CreateUserClientAsync();
        var account = await OpenFundedAccountAsync(alice, 100m);
        var key = Guid.NewGuid().ToString();

        var first = await alice.SendAsync(Withdrawal(account, 30m, key));
        var retry = await alice.SendAsync(Withdrawal(account, 30m, key));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        (await TransactionIdAsync(retry)).Should().Be(await TransactionIdAsync(first));
        (await GetBalanceAsync(alice, account)).Should().Be(70m);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_returns_409()
    {
        var alice = await Api.CreateUserClientAsync();
        var account = await OpenFundedAccountAsync(alice, 100m);
        var key = Guid.NewGuid().ToString();

        await alice.SendAsync(Withdrawal(account, 30m, key));
        var different = await alice.SendAsync(Withdrawal(account, 40m, key));

        different.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetBalanceAsync(alice, account)).Should().Be(70m);
    }

    [Fact]
    public async Task A_write_without_the_header_returns_400_and_does_nothing()
    {
        var alice = await Api.CreateUserClientAsync();
        var account = await OpenFundedAccountAsync(alice, 100m);

        var response = await alice.SendAsync(Withdrawal(account, 30m, key: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        errors.TryGetProperty(Header, out _).Should().BeTrue();
        (await GetBalanceAsync(alice, account)).Should().Be(100m);
    }

    [Fact]
    public async Task Keys_are_per_user_so_the_same_key_from_two_users_runs_twice()
    {
        var alice = await Api.CreateUserClientAsync();
        var bob = await Api.CreateUserClientAsync();
        var aliceAccount = await OpenFundedAccountAsync(alice, 100m);
        var bobAccount = await OpenFundedAccountAsync(bob, 100m);
        const string sharedKey = "order-42";

        var fromAlice = await alice.SendAsync(Withdrawal(aliceAccount, 10m, sharedKey));
        var fromBob = await bob.SendAsync(Withdrawal(bobAccount, 10m, sharedKey));

        fromAlice.StatusCode.Should().Be(HttpStatusCode.Created);
        fromBob.StatusCode.Should().Be(HttpStatusCode.Created);
        (await GetBalanceAsync(bob, bobAccount)).Should().Be(90m);
    }

    // The test clients add a fresh key to any POST without one, so "no header" must be explicit:
    // an empty value is sent, which counts as missing.
    private static HttpRequestMessage Withdrawal(Guid accountId, decimal amount, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/accounts/{accountId}/withdrawals")
        {
            Content = JsonContent.Create(new { amount }),
        };
        request.Headers.TryAddWithoutValidation(Header, key ?? string.Empty);
        return request;
    }

    private static async Task<Guid> OpenFundedAccountAsync(HttpClient client, decimal amount)
    {
        var opened = await client.PostAsJsonAsync("/api/accounts", new { currency = "EUR" });
        var accountId = (await opened.Content.ReadFromJsonAsync<AccountCreatedResponse>())!.AccountId;
        (await client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount })).EnsureSuccessStatusCode();
        return accountId;
    }

    private static async Task<Guid> TransactionIdAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TransactionCreatedResponse>())!.TransactionId;

    private static async Task<decimal> GetBalanceAsync(HttpClient client, Guid accountId) =>
        (await client.GetFromJsonAsync<AccountBalanceResponse>($"/api/accounts/{accountId}/balance"))!.Balance;
}
