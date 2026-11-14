using System.IO;
using System.Media;
using System.Windows;
using Optivance.Core;
using Optivance.Infrastructure.Monitoring;

namespace Optivance.Desktop.Monitoring;

/// <summary>
/// Alertes de température et d'espace disque : actives tant qu'au moins une alerte est
/// cochée, y compris fenêtre fermée (zone de notification). Une notification et un son au
/// franchissement du seuil ; la valeur reste colorée dans le widget tant que le seuil est dépassé.
/// </summary>
public sealed class AlertService : IDisposable
{
    public const string CpuKey = "cpu-temp";
    public const string GpuKey = "gpu-temp";
    public const string DiskPrefix = "disk:";

    private readonly AlertTracker _tracker = new();
    private readonly Action<string, string> _notify;
    private IDisposable? _subscription;
    private DateTime _lastDiskCheck = DateTime.MinValue;

    public static AlertService? Current { get; private set; }

    /// <summary>Disques sous le seuil d'espace libre (lettre → pourcentage libre).</summary>
    public IReadOnlyDictionary<string, double> LowDisks => _lowDisks;
    private readonly Dictionary<string, double> _lowDisks = [];

    public event Action? Changed;

    public AlertService(Action<string, string> notify)
    {
        _notify = notify;
        Current = this;
        ApplySettings();
    }

    public bool IsActive(string key) => _tracker.IsActive(key);

    public void ApplySettings()
    {
        var s = App.Settings;
        var any = s.AlertCpuTemperature || s.AlertGpuTemperature || s.AlertDiskSpace;
        if (any)
        {
            _subscription ??= SystemMonitor.Shared.Subscribe(snapshot => Application.Current?.Dispatcher.BeginInvoke(() => Evaluate(snapshot)));
        }
        else
        {
            _subscription?.Dispose();
            _subscription = null;
        }
        _lastDiskCheck = DateTime.MinValue;
    }

    private void Evaluate(SystemSnapshot snapshot)
    {
        var s = App.Settings;
        var changed = false;
        void Check(string key, double? value, double threshold, double margin, bool higherIsWorse, bool enabled, Func<string> title, Func<string> message)
        {
            var wasActive = _tracker.IsActive(key);
            if (_tracker.Evaluate(key, value, threshold, margin, higherIsWorse, enabled))
            {
                Raise(title(), message());
            }
            changed |= wasActive != _tracker.IsActive(key);
        }

        Check(CpuKey, snapshot.ThermalCelsius, s.AlertCpuCelsius, 3, true, s.AlertCpuTemperature,
            () => "Température processeur élevée", () => $"{snapshot.ThermalCelsius:0} °C (seuil {s.AlertCpuCelsius} °C).");
        Check(GpuKey, snapshot.GpuCelsius, s.AlertGpuCelsius, 3, true, s.AlertGpuTemperature,
            () => "Température carte graphique élevée", () => $"{snapshot.GpuCelsius:0} °C (seuil {s.AlertGpuCelsius} °C).");

        // Espace disque : une vérification par minute suffit.
        if (DateTime.UtcNow - _lastDiskCheck > TimeSpan.FromMinutes(1))
        {
            _lastDiskCheck = DateTime.UtcNow;
            foreach (var drive in FixedDrives())
            {
                var free = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
                var letter = drive.Name.TrimEnd('\\');
                Check(DiskPrefix + letter, free, s.AlertDiskFreePercent, 2, false, s.AlertDiskSpace,
                    () => $"Espace disque faible sur {letter}", () => $"{free:0.#} % libre ({Core.Storage.StorageFormat.Bytes((ulong)drive.AvailableFreeSpace)}), seuil {s.AlertDiskFreePercent} %.");
                if (_tracker.IsActive(DiskPrefix + letter)) { _lowDisks[letter] = free; } else { _lowDisks.Remove(letter); }
            }
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public static IEnumerable<DriveInfo> FixedDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            bool usable;
            try
            {
                usable = drive.DriveType == DriveType.Fixed && drive.IsReady && drive.TotalSize > 0;
            }
            catch (IOException)
            {
                usable = false;
            }
            if (usable)
            {
                yield return drive;
            }
        }
    }

    private void Raise(string title, string message)
    {
        if (App.Settings.AlertNotification)
        {
            _notify(title, message);
        }
        if (App.Settings.AlertSound)
        {
            SystemSounds.Exclamation.Play();
        }
        App.History.Add(HistoryCategory.Monitoring, "Alerte", title, message);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        if (Current == this)
        {
            Current = null;
        }
    }
}
