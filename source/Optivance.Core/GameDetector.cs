namespace Optivance.Core;

/// <summary>Fenêtre au premier plan, relevée une fois par seconde.</summary>
public sealed record ForegroundSample(int ProcessId, string ProcessName, bool Fullscreen, double? Fps);

/// <summary>
/// Détection d'un jeu au premier plan pour l'affichage automatique du widget : fenêtre plein
/// écran (ou sans bordure) qui affiche au moins <see cref="MinimumFps"/> images/s, hors
/// navigateurs, lecteurs vidéo et OPTIVANCE. Entrée après deux relevés concordants ; sortie
/// immédiate si l'on quitte la fenêtre (alt-tab, fermeture), après <see cref="LoadingGraceSeconds"/>
/// s si le jeu cesse d'afficher des images (écran de chargement).
/// </summary>
public sealed class GameDetector
{
    public const double MinimumFps = 20;
    public const int LoadingGraceSeconds = 6;

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navigateurs
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "arc",
        // Vidéo et diffusion
        "vlc", "mpc-hc", "mpc-hc64", "mpc-be64", "PotPlayerMini", "PotPlayerMini64", "wmplayer", "Video.UI", "Microsoft.Media.Player",
        "Netflix", "Disney+", "PrimeVideo", "mpv", "kodi", "obs64", "Spotify",
        // Bureau et système
        "explorer", "dwm", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "LockApp", "ApplicationFrameHost", "TextInputHost",
        "OPTIVANCE",
    };

    private int? _candidate;
    private int _candidateHits;
    private DateTime _lastFrames;

    /// <summary>Processus du jeu détecté (null : pas de jeu).</summary>
    public int? GameProcessId { get; private set; }

    public string? GameName { get; private set; }

    public static bool IsExcluded(string processName) => Excluded.Contains(processName);

    /// <summary>Met à jour l'état ; renvoie vrai si l'état « en jeu » a changé.</summary>
    public bool Update(ForegroundSample? sample, DateTime now)
    {
        var before = GameProcessId;
        var rendering = sample is { Fullscreen: true, Fps: >= MinimumFps } && !IsExcluded(sample.ProcessName);

        if (GameProcessId is { } game)
        {
            if (sample is null || sample.ProcessId != game || !sample.Fullscreen)
            {
                Leave();
            }
            else if (rendering)
            {
                _lastFrames = now;
            }
            else if ((now - _lastFrames).TotalSeconds > LoadingGraceSeconds)
            {
                Leave();
            }
        }
        else if (rendering)
        {
            _candidateHits = _candidate == sample!.ProcessId ? _candidateHits + 1 : 1;
            _candidate = sample.ProcessId;
            if (_candidateHits >= 2)
            {
                GameProcessId = sample.ProcessId;
                GameName = sample.ProcessName;
                _lastFrames = now;
            }
        }
        else
        {
            _candidate = null;
            _candidateHits = 0;
        }
        return before != GameProcessId;
    }

    private void Leave()
    {
        GameProcessId = null;
        GameName = null;
        _candidate = null;
        _candidateHits = 0;
    }
}
