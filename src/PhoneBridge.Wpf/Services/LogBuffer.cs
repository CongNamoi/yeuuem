using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PhoneBridge.Wpf.Services;

public sealed class LogBuffer
{
    private const int Capacity = 2000;
    private readonly ConcurrentQueue<string> _lines = new();

    public event Action<string>? LineAdded;

    public void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > Capacity && _lines.TryDequeue(out _)) { }
        LineAdded?.Invoke(line);
    }

    public string[] Snapshot() => _lines.ToArray();
}

public sealed class BufferLoggerProvider : ILoggerProvider
{
    private readonly LogBuffer _buffer;
    public BufferLoggerProvider(LogBuffer buffer) => _buffer = buffer;
    public ILogger CreateLogger(string categoryName) => new BufferLogger(_buffer, categoryName);
    public void Dispose() { }

    private sealed class BufferLogger : ILogger
    {
        private readonly LogBuffer _buffer;
        private readonly string _category;
        public BufferLogger(LogBuffer buffer, string category) { _buffer = buffer; _category = category[(category.LastIndexOf('.') + 1)..]; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel.ToString()[..3].ToUpperInvariant()}] {_category}: {msg}";
            if (exception != null) line += $" | {exception.GetType().Name}: {exception.Message}";
            _buffer.Add(line);
        }
    }
}
