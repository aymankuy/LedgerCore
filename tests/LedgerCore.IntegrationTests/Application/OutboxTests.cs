using System.Text.Json;
using FluentAssertions;
using LedgerCore.Application.Ledger.Deposit;
using LedgerCore.Application.Ledger.Withdraw;
using LedgerCore.Domain.Accounts;
using LedgerCore.Domain.Ledger;
using LedgerCore.Infrastructure.Persistence.Records;
using LedgerCore.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LedgerCore.IntegrationTests.Application;

/// <summary>Outbox pattern, through the real commands: every posted transaction leaves one event.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class OutboxTests(SqlServerFixture fixture)
{
    // A currency no other test class uses, so this class owns its settlement account.
    private const string Currency = "CAD";

    private readonly LedgerTestApp _app = new(fixture);

    [Fact]
    public async Task A_withdrawal_leaves_one_unprocessed_event_naming_the_transaction_and_its_accounts()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        await _app.SendAsync(new DepositCommand(accountId, 100m));

        var transactionId = await _app.SendAsync(new WithdrawCommand(accountId, 30m));

        var events = await EventsForAsync(transactionId);
        var message = events.Should().ContainSingle().Subject;
        message.Type.Should().Be(nameof(LedgerTransactionPosted));
        message.ProcessedAtUtc.Should().BeNull();

        var posted = JsonSerializer.Deserialize<LedgerTransactionPosted>(message.Payload)!;
        posted.Type.Should().Be(TransactionType.Withdrawal);
        posted.AccountIds.Should().HaveCount(2).And.Contain(accountId);
    }

    [Fact]
    public async Task A_rejected_withdrawal_leaves_no_event()
    {
        var accountId = await _app.OpenAccountAsync(Currency);
        var before = await CountEventsMentioningAsync(accountId);

        var act = () => _app.SendAsync(new WithdrawCommand(accountId, 30m));

        await act.Should().ThrowAsync<InsufficientFundsException>();
        (await CountEventsMentioningAsync(accountId)).Should().Be(before);
    }

    private async Task<List<OutboxMessage>> EventsForAsync(Guid transactionId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.OutboxMessages.Where(m => m.Payload.Contains(transactionId.ToString())).ToListAsync();
    }

    private async Task<int> CountEventsMentioningAsync(Guid accountId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.OutboxMessages.CountAsync(m => m.Payload.Contains(accountId.ToString()));
    }
}
