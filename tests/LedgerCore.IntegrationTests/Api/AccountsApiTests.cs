using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LedgerCore.Api.Controllers;
using LedgerCore.Application.Accounts.GetBalance;
using LedgerCore.Application.Accounts.GetStatement;
using LedgerCore.Application.Accounts.ListAccounts;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Api;

/// <summary>
/// End-to-end over HTTP: JWT, the MediatR pipeline, handlers and SQL Server together.
/// Proves the Phase 1 acceptance criteria on access control and on 422 for overdrafts.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AccountsApiTests(SqlServerFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    [Fact]
    public async Task Requests_without_a_token_are_rejected()
    {
        var response = await Api.CreateClient().GetAsync("/api/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_user_cannot_read_or_move_money_on_another_users_account()
    {
        var alice = await Api.CreateUserClientAsync();
        var bob = await Api.CreateUserClientAsync();
        var aliceAccount = await OpenFundedAccountAsync(alice, 100m);

        var balance = await bob.GetAsync($"/api/accounts/{aliceAccount}/balance");
        var statement = await bob.GetAsync($"/api/accounts/{aliceAccount}/statement");
        var withdrawal = await bob.PostAsJsonAsync($"/api/accounts/{aliceAccount}/withdrawals", new { amount = 10m });
        var transfer = await bob.PostAsJsonAsync("/api/transfers", new
        {
            sourceAccountId = aliceAccount,
            destinationAccountId = await OpenAccountAsync(bob),
            amount = 10m,
        });

        // Reported exactly like a missing account, so ids cannot be probed.
        balance.StatusCode.Should().Be(HttpStatusCode.NotFound);
        statement.StatusCode.Should().Be(HttpStatusCode.NotFound);
        withdrawal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        transfer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetBalanceAsync(alice, aliceAccount)).Should().Be(100m);
    }

    [Fact]
    public async Task An_admin_can_read_any_account()
    {
        var alice = await Api.CreateUserClientAsync();
        var aliceAccount = await OpenFundedAccountAsync(alice, 100m);
        var admin = await Api.CreateAdminClientAsync();

        (await GetBalanceAsync(admin, aliceAccount)).Should().Be(100m);
    }

    [Fact]
    public async Task Only_an_admin_can_freeze_and_a_frozen_account_cannot_move_money()
    {
        var alice = await Api.CreateUserClientAsync();
        var aliceAccount = await OpenFundedAccountAsync(alice, 100m);
        var admin = await Api.CreateAdminClientAsync();

        var byOwner = await alice.PostAsync($"/api/accounts/{aliceAccount}/freeze", content: null);
        var byAdmin = await admin.PostAsync($"/api/accounts/{aliceAccount}/freeze", content: null);
        var whileFrozen = await alice.PostAsJsonAsync($"/api/accounts/{aliceAccount}/withdrawals", new { amount = 10m });
        var unfreeze = await admin.PostAsync($"/api/accounts/{aliceAccount}/unfreeze", content: null);
        var afterUnfreeze = await alice.PostAsJsonAsync($"/api/accounts/{aliceAccount}/withdrawals", new { amount = 10m });

        byOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        byAdmin.StatusCode.Should().Be(HttpStatusCode.NoContent);
        whileFrozen.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemCodeAsync(whileFrozen)).Should().Be("account_not_active");
        unfreeze.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterUnfreeze.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Transferring_more_than_the_balance_returns_422()
    {
        var alice = await Api.CreateUserClientAsync();
        var source = await OpenFundedAccountAsync(alice, 50m);
        var destination = await OpenAccountAsync(await Api.CreateUserClientAsync());

        var response = await alice.PostAsJsonAsync("/api/transfers", new
        {
            sourceAccountId = source,
            destinationAccountId = destination,
            amount = 80m,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ProblemCodeAsync(response)).Should().Be("insufficient_funds");
        (await GetBalanceAsync(alice, source)).Should().Be(50m);
    }

    [Fact]
    public async Task Invalid_amount_returns_400_with_field_errors()
    {
        var alice = await Api.CreateUserClientAsync();
        var account = await OpenAccountAsync(alice);

        var response = await alice.PostAsJsonAsync($"/api/accounts/{account}/deposits", new { amount = -5m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty("Amount", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Deposit_transfer_and_statement_work_end_to_end()
    {
        var alice = await Api.CreateUserClientAsync();
        var bob = await Api.CreateUserClientAsync();
        var aliceAccount = await OpenFundedAccountAsync(alice, 200m);
        var bobAccount = await OpenAccountAsync(bob);

        var transfer = await alice.PostAsJsonAsync("/api/transfers", new
        {
            sourceAccountId = aliceAccount,
            destinationAccountId = bobAccount,
            amount = 60m,
            description = "Dinner",
        });
        transfer.StatusCode.Should().Be(HttpStatusCode.Created);

        var accounts = await alice.GetFromJsonAsync<List<AccountSummary>>("/api/accounts");
        accounts.Should().ContainSingle().Which.Balance.Should().Be(140m);

        var statement = await alice.GetFromJsonAsync<AccountStatementResponse>($"/api/accounts/{aliceAccount}/statement");
        statement!.Lines.Select(l => l.Amount).Should().Equal(200m, -60m);
        statement.ClosingBalance.Should().Be(140m);
        (await GetBalanceAsync(bob, bobAccount)).Should().Be(60m);
    }

    private static async Task<Guid> OpenAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { currency = "EUR" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AccountCreatedResponse>())!.AccountId;
    }

    private static async Task<Guid> OpenFundedAccountAsync(HttpClient client, decimal amount)
    {
        var accountId = await OpenAccountAsync(client);
        var deposit = await client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount });
        deposit.StatusCode.Should().Be(HttpStatusCode.Created);
        return accountId;
    }

    private static async Task<decimal> GetBalanceAsync(HttpClient client, Guid accountId) =>
        (await client.GetFromJsonAsync<AccountBalanceResponse>($"/api/accounts/{accountId}/balance"))!.Balance;

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
