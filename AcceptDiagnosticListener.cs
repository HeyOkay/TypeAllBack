using System.Collections.Concurrent;
using System.Diagnostics;
namespace LayoutPreviewPrototype;
internal sealed class AcceptDiagnosticListener : TraceListener
{
    private readonly ConcurrentQueue<(DateTime Time, long SessionId, string Detail)> _messages = new();
    private long _sessionId;
    public void Begin(long sessionId) => Interlocked.Exchange(ref _sessionId, sessionId);
    public override void Write(string? message) { }
    public override void WriteLine(string? message)
    {
        if (message?.StartsWith("Accept check: ", StringComparison.Ordinal) != true) return;
        _messages.Enqueue((DateTime.Now, Interlocked.Read(ref _sessionId), message[14..]));
        while (_messages.Count > SessionDiagnostics.MaximumEvents) _messages.TryDequeue(out _);
    }
    public IEnumerable<(DateTime Time, long SessionId, string Detail)> Drain()
    { while (_messages.TryDequeue(out var message)) yield return message; }
}
