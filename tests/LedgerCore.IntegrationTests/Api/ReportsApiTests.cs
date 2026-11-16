using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LedgerCore.Api.Controllers;
using LedgerCore.Application.Reports;
using LedgerCore.Domain.Ledger;
using LedgerCore.IntegrationTests.Infrastructure;

namespace LedgerCore.IntegrationTests.Api;

/// <summary>
/// The report stored procedures over HTTP. Other tests share the database, so each test compares
/// the report before and after its own postings, or uses an amount no other test uses.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ReportsApiTests(SqlServerFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    [Fact]
    public async Task Reports_are_for_admins_only()
    {
        var user = await Api.CreateUserClientAsync();

        (await user.GetAsync("/api/reports/daily-volume")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.GetAsync("/api/reports/top-accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Daily_volume_counts_each_transaction_once_with_its_amount()
    {
        var admin = await Api.CreateAdminClientAsync();
        var user = await Api.CreateUserClientAsync();
        var account = await OpenAccountAsync(user, "USD");

        var before = await TodaysUsdDepositsAsync(admin);
        await DepositAsync(user, account, 100.25m);
        await DepositAsync(user, account, 20m);
        var after = await TodaysUsdDepositsAsync(admin);

        after.Transactions.Should().Be(before.Transactions + 2);
        after.Volume.Should().Be(before.Volume + 120.25m);
    }

    [Fact]
    public async Task Top_accounts_ranks_customer_accounts_by_money_moved()
    {
        var admin = await Api.CreateAdminClientAsync();
        var user = await Api.CreateUserClientAsync();
        var account = await OpenAccountAsync(user, "TRY");
        await DepositAsync(user, account, 987_654_321m);

        var top = await admin.GetFromJsonAsync<List<TopAccountRow>>("/api/reports/top-accounts?currency=TRY&top=1");

        var row = top.Should().ContainSingle().Subject;
        row.AccountId.Should().Be(account);
        row.MoneyIn.Should().Be(987_654_321m);
        row.MoneyOut.Should().Be(0m);
        row.Entries.Should().Be(1);
    }

    [Fact]
    public async Task An_invalid_window_is_a_400()
    {
        var admin = await Api.CreateAdminClientAsync();

        var response = await admin.GetAsync("/api/reports/daily-volume?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<(long Transactions, decimal Volume)> TodaysUsdDepositsAsync(HttpClient admin)
    {
        var rows = await admin.GetFromJsonAsync<List<DailyVolumeRow>>("/api/reports/daily-volume?currency=USD");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deposits = rows!.Where(r => r.Day == today && r.Type == TransactionType.Deposit).ToList();
        return (deposits.Sum(r => r.Transactions), deposits.Sum(r => r.Volume));
    }

    private static async Task<Guid> OpenAccountAsync(HttpClient client, string currency)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { currency });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AccountCreatedResponse>())!.AccountId;
    }

    private static async Task DepositAsync(HttpClient client, Guid accountId, decimal amount)
    {
        var response = await client.PostAsJsonAsync($"/api/accounts/{accountId}/deposits", new { amount });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
