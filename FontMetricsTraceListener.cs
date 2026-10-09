using System.Diagnostics;

namespace LayoutPreviewPrototype;

// Opt-in diagnostics capture font metadata only, never document text or key logs.
internal sealed class FontMetricsTraceListener(string path) : TextWriterTraceListener(path)
{
    public override void Write(string? message)
    {
        if (message?.StartsWith("Font metrics:", StringComparison.Ordinal) == true) base.Write(message);
    }
    public override void WriteLine(string? message)
    {
        if (message?.StartsWith("Font metrics:", StringComparison.Ordinal) == true) base.WriteLine(message);
    }
}
