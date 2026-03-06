using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Fail2Ban.API.Services;

public class MemoryLogger : ILogger
{
    private readonly string _name;
    private readonly MemoryLoggerProvider _provider;

    public MemoryLogger(string name, MemoryLoggerProvider provider)
    {
        _name = name;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        
        var message = formatter(state, exception);
        var logEntry = $"[{DateTime.Now:HH:mm:ss}] [{logLevel}] {message}";
        _provider.AddLog(logEntry);
    }
}

public class MemoryLoggerProvider : ILoggerProvider
{
    private static readonly ConcurrentQueue<string> _logs = new();
    private const int MaxLogCount = 100;

    public ILogger CreateLogger(string categoryName)
    {
        return new MemoryLogger(categoryName, this);
    }

    public void AddLog(string message)
    {
        _logs.Enqueue(message);
        while (_logs.Count > MaxLogCount)
        {
            _logs.TryDequeue(out _);
        }
    }

    public List<string> GetLogs()
    {
        return _logs.ToList();
    }

    public void Dispose() { }
}
