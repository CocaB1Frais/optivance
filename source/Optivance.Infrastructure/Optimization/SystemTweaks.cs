using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Optivance.Core;

namespace Optivance.Infrastructure.Optimization;

// ───────────────────────────── Applications au démarrage ─────────────────────────────

public enum StartupLocation { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

public sealed record StartupApp(string Name, string Command, StartupLocation Location, bool Enabled)
{
    /// <summary>Les entrées « pour tous les utilisateurs » passent par le composant administrateur.</summary>
    public bool RequiresAdmin => Location is StartupLocation.MachineRun or StartupLocation.MachineRun32 or StartupLocation.CommonFolder;

    /// <summary>Le dossier Démarrage commun n'est pas dans la liste du composant administrateur.</summary>
    public bool CanToggle => Location != StartupLocation.CommonFolder;

    public string LocationLabel => Location switch
    {
        StartupLocation.UserRun => "Votre compte",
        StartupLocation.UserFolder => "Dossier Démarrage",
        StartupLocation.CommonFolder => "Dossier Démarrage (tous)",
        _ => "Tous les utilisateurs"
    };
}

public static class StartupApps
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static IReadOnlyList<StartupApp> List()
    {
        List<StartupApp> apps = [];
        void FromKey(RegistryKey hive, string path, string approvedKey, StartupLocation location)
        {
            using var key = hive.OpenSubKey(path);
            using var approved = hive.OpenSubKey(Approved + approvedKey);
            if (key is null)
            {
                return;
            }
            foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
            {
                var enabled = OptimizationCatalog.IsStartupApproved(approved?.GetValue(name) as byte[]);
                apps.Add(new StartupApp(name, key.GetValue(name)?.ToString() ?? "", location, enabled));
            }
        }
        void FromFolder(string folder, RegistryKey hive, StartupLocation location)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }
            using var approved = hive.OpenSubKey(Approved + "StartupFolder");
            foreach (var file in Directory.EnumerateFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(file);
                apps.Add(new StartupApp(name, file, location, OptimizationCatalog.IsStartupApproved(approved?.GetValue(name) as byte[])));
            }
        }
        FromKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", StartupLocation.UserRun);
        FromKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run", StartupLocation.MachineRun);
        FromKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", StartupLocation.MachineRun32);
        FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, StartupLocation.UserFolder);
        FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, StartupLocation.CommonFolder);
        return apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Entrées de votre compte : écrites directement (HKCU), comme le Gestionnaire des tâches.</summary>
    public static void SetUserEnabled(StartupApp app, bool enabled)
    {
        var sub = app.Location switch
        {
            StartupLocation.UserRun => "Run",
            StartupLocation.UserFolder => "StartupFolder",
            _ => throw new InvalidOperationException("Entrée réservée au composant administrateur.")
        };
        using var key = Registry.CurrentUser.CreateSubKey(Approved + sub);
        key.SetValue(app.Name, OptimizationCatalog.StartupApprovedValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
    }

    /// <summary>Commande pour le composant administrateur (entrées HKLM uniquement).</summary>
    public static string MachineCommand(StartupApp app, bool enabled)
    {
        var key = app.Location == StartupLocation.MachineRun32 ? "Run32" : "Run";
        var name = app.Name.Replace("\"", "");
        return $"{{\"v\":3,\"type\":\"tune-startup\",\"key\":\"{key}\",\"name\":\"{name}\",\"enabled\":\"{(enabled ? "true" : "false")}\"}}";
    }

    /// <summary>Nom transmissible au composant (sans guillemet, barre oblique inverse ni caractère de contrôle).</summary>
    public static bool CanSendToHelper(StartupApp app) =>
        app.Name.Length is > 0 and <= 260 && !app.Name.Any(c => char.IsControl(c) || c is '"' or '\\');
}

// ───────────────────────────── Modes d'alimentation ─────────────────────────────

public sealed record PowerPlan(Guid Id, string Name, bool Active);

public static class PowerPlans
{
    public static IReadOnlyList<PowerPlan> List()
    {
        var active = Active();
        List<PowerPlan> plans = [];
        var buffer = new byte[16];
        for (uint index = 0; ; index++)
        {
            uint size = 16;
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16 /* ACCESS_SCHEME */, index, buffer, ref size) != 0)
            {
                break;
            }
            var id = new Guid(buffer);
            plans.Add(new PowerPlan(id, FriendlyName(id), id == active));
        }
        return plans;
    }

    public static Guid? Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }
        var id = Marshal.PtrToStructure<Guid>(pointer);
        LocalFree(pointer);
        return id;
    }

    public static bool SetActive(Guid id) => PowerSetActiveScheme(IntPtr.Zero, ref id) == 0;

    private static string FriendlyName(Guid id)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0)
        {
            return id.ToString();
        }
        var buffer = new byte[size];
        return PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
            ? Encoding.Unicode.GetString(buffer).TrimEnd('\0')
            : id.ToString();
    }

    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint accessFlags, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}

// ───────────────────────────── Réglages de votre compte (HKCU) ─────────────────────────────

public sealed record RegistryValue(string Key, string Name, RegistryValueKind Kind, object Optimized);

public sealed record UserTweak(string Id, string Title, string Description, TweakRisk Risk, string Consequence, IReadOnlyList<RegistryValue> Values, Action? AfterChange = null);

public static class UserTweaks
{
    public static IReadOnlyList<UserTweak> All { get; } =
    [
        new("game-mode", "Mode Jeu", "Windows donne la priorité au jeu en cours (processeur, carte graphique).", TweakRisk.Safe, "Aucune conséquence notable.",
            [new(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", RegistryValueKind.DWord, 1)]),
        new("game-dvr", "Désactiver l’enregistrement en arrière-plan des jeux", "La Xbox Game Bar n’enregistre plus en continu les dernières minutes de jeu.", TweakRisk.Moderate,
            "Win+Alt+R et « Enregistrer ce qui vient de se passer » ne fonctionnent plus ; la Game Bar reste disponible.",
            [new(@"System\GameConfigStore", "GameDVR_Enabled", RegistryValueKind.DWord, 0), new(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", RegistryValueKind.DWord, 0)]),
        new("transparency", "Désactiver la transparence", "Barre des tâches et menus opaques : un peu moins de travail pour la carte graphique.", TweakRisk.Safe, "Apparence plus sobre ; aucune autre conséquence.",
            [new(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValueKind.DWord, 0)]),
        new("animations", "Désactiver l’animation des fenêtres", "Ouverture, réduction et agrandissement instantanés.", TweakRisk.Safe, "Aucune conséquence, à part l’effet visuel.",
            [new(@"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryValueKind.String, "0")], NativeSettings.ApplyAnimation),
        new("mouse-precision", "Désactiver l’accélération de la souris", "Le pointeur se déplace proportionnellement au mouvement (préféré dans les jeux).", TweakRisk.Safe,
            "Le pointeur peut sembler plus lent : ajustez la vitesse dans les paramètres de la souris si besoin.",
            [new(@"Control Panel\Mouse", "MouseSpeed", RegistryValueKind.String, "0"), new(@"Control Panel\Mouse", "MouseThreshold1", RegistryValueKind.String, "0"), new(@"Control Panel\Mouse", "MouseThreshold2", RegistryValueKind.String, "0")],
            NativeSettings.ApplyMouse),
        new("advertising-id", "Désactiver l’identifiant de publicité", "Les applications ne peuvent plus vous proposer de publicités personnalisées.", TweakRisk.Safe, "Aucune conséquence fonctionnelle.",
            [new(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", RegistryValueKind.DWord, 0)]),
        new("suggestions", "Désactiver les suggestions et applications promues", "Plus de suggestions dans le menu Démarrer ni d’installations automatiques d’applications promues.", TweakRisk.Safe, "Aucune conséquence fonctionnelle.",
            [
                new(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", RegistryValueKind.DWord, 0),
                new(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", RegistryValueKind.DWord, 0),
                new(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", RegistryValueKind.DWord, 0),
                new(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", RegistryValueKind.DWord, 0)
            ]),
    ];

    public static string BackupKey(RegistryValue value) => $"hkcu:{value.Key}|{value.Name}";

    public static bool IsOptimized(UserTweak tweak) => tweak.Values.All(value =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(value.Key);
        return Equals(key?.GetValue(value.Name)?.ToString(), value.Optimized.ToString());
    });

    /// <summary>Valeur actuelle sérialisée (« absent » si la valeur n'existe pas).</summary>
    public static string Snapshot(RegistryValue value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(value.Key);
        var current = key?.GetValue(value.Name);
        return current is null ? "absent" : $"{key!.GetValueKind(value.Name)}:{current}";
    }

    public static void Apply(UserTweak tweak, OptimizationBackup backup)
    {
        foreach (var value in tweak.Values)
        {
            backup.Remember(BackupKey(value), Snapshot(value));
            using var key = Registry.CurrentUser.CreateSubKey(value.Key);
            key.SetValue(value.Name, value.Optimized, value.Kind);
        }
        tweak.AfterChange?.Invoke();
        NativeSettings.Broadcast();
    }

    /// <summary>Revient aux valeurs d'avant OPTIVANCE (ou retire la valeur si elle n'existait pas).</summary>
    public static void Restore(UserTweak tweak, OptimizationBackup backup)
    {
        foreach (var value in tweak.Values)
        {
            if (!backup.TryGet(BackupKey(value), out var original))
            {
                continue;
            }
            using var key = Registry.CurrentUser.CreateSubKey(value.Key);
            if (original == "absent")
            {
                key.DeleteValue(value.Name, throwOnMissingValue: false);
            }
            else
            {
                var separator = original.IndexOf(':');
                var kind = Enum.Parse<RegistryValueKind>(original[..separator]);
                var text = original[(separator + 1)..];
                key.SetValue(value.Name, kind == RegistryValueKind.DWord ? int.Parse(text) : text, kind);
            }
            backup.Forget(BackupKey(value));
        }
        tweak.AfterChange?.Invoke();
        NativeSettings.Broadcast();
    }
}

/// <summary>Applique immédiatement les réglages lus dans le registre (sans redémarrage de session).</summary>
internal static class NativeSettings
{
    private const uint SpiSetMouse = 0x0004;
    private const uint SpiSetAnimation = 0x0049;
    private const uint SpifSendChange = 0x2;

    public static void ApplyAnimation()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics");
        var info = new AnimationInfo { Size = 8, MinAnimate = key?.GetValue("MinAnimate")?.ToString() == "0" ? 0 : 1 };
        SystemParametersInfoW(SpiSetAnimation, 8, ref info, SpifSendChange);
    }

    public static void ApplyMouse()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
        int Read(string name, int fallback) => int.TryParse(key?.GetValue(name)?.ToString(), out var v) ? v : fallback;
        var values = new[] { Read("MouseThreshold1", 6), Read("MouseThreshold2", 10), Read("MouseSpeed", 1) };
        SystemParametersInfoW(SpiSetMouse, 0, values, SpifSendChange);
    }

    public static void Broadcast() =>
        SendMessageTimeoutW(new IntPtr(0xFFFF), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 0x2, 500, out _);

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo { public uint Size; public int MinAnimate; }

    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(uint action, uint param, ref AnimationInfo info, uint flags);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfoW(uint action, uint param, int[] values, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

// ───────────────────────────── Services et tâches (lecture) ─────────────────────────────

public static class SystemState
{
    public static string ServiceMode(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        if (key is null)
        {
            return "missing";
        }
        return OptimizationCatalog.StartMode(key.GetValue("Start") as int?, key.GetValue("DelayedAutostart") as int?);
    }

    /// <summary>true = activée, false = désactivée, null = introuvable ou illisible sans droits.</summary>
    public static bool? TaskEnabled(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                ArgumentList = { "/Query", "/TN", path, "/XML" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return null;
            }
            using var memory = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(memory);
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return null;
            }
            var bytes = memory.ToArray();
            var xml = bytes.Length > 1 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
            var settings = Regex.Match(xml, "<Settings>(.*?)</Settings>", RegexOptions.Singleline);
            var enabled = Regex.Match(settings.Success ? settings.Groups[1].Value : xml, "<Enabled>(true|false)</Enabled>");
            return !enabled.Success || enabled.Groups[1].Value == "true";
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
