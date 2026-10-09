using System.Windows;

namespace LayoutPreviewPrototype;

public partial class App : Application
{
    private LayoutPreviewController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int performance = Array.IndexOf(e.Args, "--performance-diagnostics");
        if (performance >= 0 && performance + 1 < e.Args.Length)
            PerformanceDiagnostics.Configure(e.Args[performance + 1]);
        int fontDiagnostic = Array.IndexOf(e.Args, "--font-diagnostics");
        if (fontDiagnostic >= 0 && fontDiagnostic + 1 < e.Args.Length)
        {
            System.Diagnostics.Trace.Listeners.Add(new FontMetricsTraceListener(e.Args[fontDiagnostic + 1]));
            System.Diagnostics.Trace.AutoFlush = true;
        }
        int diagnostic = Array.IndexOf(e.Args, "--diagnostics");
        if (diagnostic >= 0 && diagnostic + 1 < e.Args.Length)
        {
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(e.Args[diagnostic + 1]));
            System.Diagnostics.Trace.AutoFlush = true;
        }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _controller = new LayoutPreviewController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}
