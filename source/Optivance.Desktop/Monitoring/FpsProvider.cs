using System.Diagnostics;
using System.Runtime.InteropServices;
using Optivance.Core;
using Optivance.Infrastructure;

namespace Optivance.Desktop.Monitoring;

/// <summary>
/// FPS et détection du jeu au premier plan : interroge le composant administrateur une fois par
/// seconde, quand le widget ou le Monitoring lisent la valeur, ou en continu si l'affichage
/// automatique du widget en jeu est activé. La session ETW du composant est démarrée à la
/// première demande et arrêtée avec lui.
/// </summary>
public sealed class FpsProvider : IDisposable
{
    private readonly Func<MftSession?> _session;
    private readonly Timer _timer;
    private readonly GameDetector _detector = new();
    private readonly int _ownProcess = Environment.ProcessId;
    private long _lastRead;
    private MftSession? _started;
    private int _polling;
    private (double? Fps, string? Process) _value;
    private (int Pid, string Name) _name;

    /// <summary>Entrée (nom du jeu) ou sortie (null) d'un jeu ; appelé hors du fil d'interface.</summary>
    public event Action<string?>? GameChanged;

    public FpsProvider(Func<MftSession?> session)
    {
        _session = session;
        _timer = new Timer(_ => _ = PollAsync(), null, 1000, 1000);
    }

    /// <summary>Faux si l'autorisation administrateur n'est pas accordée (mesure impossible).</summary>
    public bool Available => _session() is not null;

    public bool InGame => _detector.GameProcessId is not null;

    /// <summary>Appelé par le Monitoring à chaque relevé.</summary>
    public (double? Fps, string? Process) Read()
    {
        Interlocked.Exchange(ref _lastRead, Environment.TickCount64);
        return _value;
    }

    private async Task PollAsync()
    {
        var wanted = Environment.TickCount64 - Interlocked.Read(ref _lastRead) <= 3000 || App.Settings.WidgetAutoShowInGames;
        if (!wanted || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }
        try
        {
            var sample = await SampleAsync();
            _value = sample is { Fps: >= 1 } ? (sample.Fps, sample.ProcessName) : (null, null);
            if (App.Settings.WidgetAutoShowInGames || InGame)
            {
                if (_detector.Update(sample, DateTime.UtcNow))
                {
                    GameChanged?.Invoke(_detector.GameName);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private async Task<ForegroundSample?> SampleAsync()
    {
        var session = _session();
        if (session is null)
        {
            return null;
        }
        if (!ReferenceEquals(session, _started))
        {
            if (!await session.StartFpsAsync())
            {
                return null;
            }
            _started = session;
        }
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }
        GetWindowThreadProcessId(window, out var pid);
        if (pid == 0 || pid == _ownProcess)
        {
            return null;
        }
        var fps = await session.QueryFpsAsync((int)pid);
        return new ForegroundSample((int)pid, NameOf((int)pid), IsFullscreen(window), fps);
    }

    /// <summary>Fenêtre qui couvre tout son écran (plein écran ou sans bordure), hors bureau.</summary>
    private static bool IsFullscreen(IntPtr window)
    {
        if (window == GetShellWindow() || window == GetDesktopWindow() || !GetWindowRect(window, out var rect))
        {
            return false;
        }
        var monitor = MonitorFromWindow(window, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }
        var screen = info.Monitor;
        return rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }

    private string NameOf(int pid)
    {
        if (_name.Pid != pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                _name = (pid, process.ProcessName);
            }
            catch (ArgumentException)
            {
                _name = (pid, $"processus {pid}");
            }
        }
        return _name.Name;
    }

    public void Dispose() => _timer.Dispose();

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
}
