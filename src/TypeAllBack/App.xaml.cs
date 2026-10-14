using System.Windows;

namespace LayoutPreviewPrototype;

public partial class App : Application
{
    private LayoutPreviewController? _controller;
    private Mutex? _instanceMarker;
    private TrayHost? _tray;
    private CorrectionSound? _sound;
    private TraySettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--uninstall-cleanup"))
        {
            TrayHost.CleanupStartup();
            Shutdown();
            return;
        }
        // Test window only: no instance marker, keyboard hook or tray in this process.
        if (e.Args.Contains(FieldTesterWindow.Argument))
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            GlassPreviewOptions.SetEnabled(TraySettings.Load().GlassPreview);
            MainWindow = FieldTesterWindow.Create();
            MainWindow.Show();
            return;
        }
        // Keep the named object alive for the whole application lifetime.
        // No mutex ownership/wait is needed: only its atomic creation reserves
        // the slot. A second process exits before installing a keyboard hook.
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        _instanceMarker = new Mutex(false, $@"Local\TypeAllBack.LayoutPreview.{user}", out bool firstInstance);
        if (!firstInstance)
        {
            _instanceMarker.Dispose();
            _instanceMarker = null;
            Shutdown();
            return;
        }

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
        _settings = TraySettings.Load();
        _settings.Save();
        AutoEnterOptions.SetEnabled(_settings.AutoEnter);
        AutoEnterOptions.Changed += SaveAutoEnter;
        GlassPreviewOptions.SetEnabled(_settings.GlassPreview);
        GlassPreviewOptions.Changed += SaveGlassPreview;
        NewSessionAfterAcceptOptions.SetEnabled(_settings.NewSessionAfterAccept);
        NewSessionAfterAcceptOptions.Changed += SaveNewSessionAfterAccept;
        _sound = new CorrectionSound(_settings.Sound);
        _controller = new LayoutPreviewController();
        _controller.ReplacementConfirmed += _sound.Play;
        _controller.Start(_settings.Enabled);
        _tray = new TrayHost(_settings, _controller.SetEnabled, _controller.ShowDebug, _sound);
    }

    private void SaveAutoEnter(bool enabled)
    {
        if (_settings == null) return;
        _settings.AutoEnter = enabled;
        _settings.Save();
    }

    private void SaveGlassPreview(bool enabled)
    {
        if (_settings == null) return;
        _settings.GlassPreview = enabled;
        _settings.Save();
    }

    private void SaveNewSessionAfterAccept(bool enabled)
    {
        if (_settings == null) return;
        _settings.NewSessionAfterAccept = enabled;
        _settings.Save();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AutoEnterOptions.Changed -= SaveAutoEnter;
        GlassPreviewOptions.Changed -= SaveGlassPreview;
        NewSessionAfterAcceptOptions.Changed -= SaveNewSessionAfterAccept;
        _tray?.Dispose();
        _controller?.Dispose();
        _sound?.Dispose();
        _instanceMarker?.Dispose();
        _instanceMarker = null;
        base.OnExit(e);
    }
}
