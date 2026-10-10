using System.IO;
using System.Runtime.InteropServices;

namespace LayoutPreviewPrototype;

// Load once; all audio API calls run outside the UI, editor and hook threads.
internal sealed class CorrectionSound : IDisposable
{
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _enabled, _disposed;
    private int _pending;
    internal bool Enabled => _enabled;
    internal CorrectionSound(bool enabled)
    {
        _enabled = enabled;
        _thread = new Thread(Run) { IsBackground = true, Name = "TypeAllBack sound" };
        _thread.Start();
    }
    internal void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        _enabled = enabled;
        if (!enabled) Interlocked.Exchange(ref _pending, 0);
        _wake.Set();
    }
    internal void Play()
    {
        if (_disposed || !_enabled) return;
        Interlocked.Exchange(ref _pending, 1);
        _wake.Set();
    }
    private void Run()
    {
        GCHandle pinned = default;
        try
        {
            using var stream = typeof(CorrectionSound).Assembly.GetManifestResourceStream("TypeAllBack.Correction.wav");
            if (stream == null) return;
            using var memory = new MemoryStream(); stream.CopyTo(memory);
            // The buffer remains pinned until asynchronous playback is stopped.
            pinned = GCHandle.Alloc(memory.ToArray(), GCHandleType.Pinned);
            while (!_disposed)
            {
                _wake.WaitOne();
                if (_disposed) break;
                if (!_enabled) { PlaySound(IntPtr.Zero, IntPtr.Zero, 0); continue; }
                if (Interlocked.Exchange(ref _pending, 0) == 1)
                    PlaySound(pinned.AddrOfPinnedObject(), IntPtr.Zero, 0x0001 | 0x0002 | 0x0004 | 0x0010);
            }
        }
        catch (Exception error) when (error is IOException or DllNotFoundException or EntryPointNotFoundException)
        { System.Diagnostics.Trace.WriteLine("Correction sound unavailable: " + error.GetType().Name); }
        finally
        {
            if (pinned.IsAllocated)
            {
                PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
                pinned.Free();
            }
        }
    }
    public void Dispose()
    {
        _disposed = true; _wake.Set();
        // The worker owns its buffer; an unfinished audio stop cannot free it early.
        _thread.Join(200);
    }
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
