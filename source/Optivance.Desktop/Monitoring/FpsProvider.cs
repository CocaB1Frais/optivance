using System.Diagnostics;
using System.Runtime.InteropServices;
using Optivance.Infrastructure;

namespace Optivance.Desktop.Monitoring;

/// <summary>
/// FPS du jeu au premier plan : interroge le composant administrateur une fois par seconde,
/// uniquement tant que le widget ou le Monitoring lisent la valeur (sinon rien ne tourne).
/// La session ETW du composant est démarrée à la première demande et arrêtée avec lui.
/// </summary>
public sealed class FpsProvider : IDisposable
{
    private readonly Func<MftSession?> _session;
    private readonly Timer _timer;
    private readonly int _ownProcess = Environment.ProcessId;
    private long _lastRead;
    private MftSession? _started;
    private int _polling;
    private (double? Fps, string? Process) _value;
    private (int Pid, string Name) _name;

    public FpsProvider(Func<MftSession?> session)
    {
        _session = session;
        _timer = new Timer(_ => _ = PollAsync(), null, 1000, 1000);
    }

    /// <summary>Faux si l'autorisation administrateur n'est pas accordée (mesure impossible).</summary>
    public bool Available => _session() is not null;

    /// <summary>Appelé par le Monitoring à chaque relevé.</summary>
    public (double? Fps, string? Process) Read()
    {
        Interlocked.Exchange(ref _lastRead, Environment.TickCount64);
        return _value;
    }

    private async Task PollAsync()
    {
        // Personne ne lit les FPS depuis 3 s : aucune requête.
        if (Environment.TickCount64 - Interlocked.Read(ref _lastRead) > 3000 || Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }
        try
        {
            var session = _session();
            if (session is null)
            {
                _value = (null, null);
                return;
            }
            if (!ReferenceEquals(session, _started))
            {
                if (!await session.StartFpsAsync())
                {
                    _value = (null, null);
                    return;
                }
                _started = session;
            }
            var pid = ForegroundProcessId();
            if (pid is null || pid == _ownProcess)
            {
                _value = (null, null);
                return;
            }
            var fps = await session.QueryFpsAsync(pid.Value);
            _value = fps is { } value && value >= 1 ? (value, NameOf(pid.Value)) : (null, null);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
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

    private static int? ForegroundProcessId()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }
        GetWindowThreadProcessId(window, out var pid);
        return pid == 0 ? null : (int)pid;
    }

    public void Dispose() => _timer.Dispose();

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
