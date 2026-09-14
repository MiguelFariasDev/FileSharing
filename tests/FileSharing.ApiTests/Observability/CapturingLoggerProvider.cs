using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FileSharing.ApiTests.Observability;

/// <summary>
/// Captures every formatted log message (and exception text, when logged) written anywhere in
/// the host during a test — used to assert, on real rendered output rather than by reading the
/// source code, that nothing sensitive (a JWT, an Authorization header, a password, a presigned
/// URL, an access token) ever reaches a log line. Deliberately ignores scope state (BeginScope)
/// for this purpose: CorrelationIdMiddleware's scope only ever carries the correlation id
/// itself, which is not a secret.
/// </summary>
public class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentBag<string> _messages = [];

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentBag<string> _messages;

        public CapturingLogger(ConcurrentBag<string> messages)
        {
            _messages = messages;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
            if (exception is not null)
                _messages.Add(exception.ToString());
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
