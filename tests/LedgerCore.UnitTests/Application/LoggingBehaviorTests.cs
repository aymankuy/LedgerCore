using LedgerCore.Application.Auth.Register;
using LedgerCore.Application.Behaviors;
using Microsoft.Extensions.Logging;

namespace LedgerCore.UnitTests.Application;

public class LoggingBehaviorTests
{
    private readonly RecordingLogger<LoggingBehavior<RegisterUserCommand, Guid>> _logger = new();
    private readonly RegisterUserCommand _request = new("ada@example.com", "very-secret-password");

    [Fact]
    public async Task A_successful_request_is_logged_once_without_its_payload()
    {
        await new LoggingBehavior<RegisterUserCommand, Guid>(_logger)
            .Handle(_request, _ => Task.FromResult(Guid.NewGuid()), CancellationToken.None);

        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().StartWith("RegisterUserCommand succeeded");
        entry.Message.Should().NotContain("very-secret-password");
    }

    [Fact]
    public async Task A_failed_request_is_logged_as_a_warning_and_the_exception_still_reaches_the_caller()
    {
        var act = () => new LoggingBehavior<RegisterUserCommand, Guid>(_logger)
            .Handle(_request, _ => throw new InvalidOperationException("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().StartWith("RegisterUserCommand failed with InvalidOperationException");
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
