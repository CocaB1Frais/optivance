namespace Optivance.Core;

/// <summary>
/// Seuils d'alerte avec hystérésis : une alerte se déclenche une fois au franchissement du
/// seuil, puis ne se redéclenche qu'après être revenue sous le seuil moins la marge
/// (pas de notifications en boucle autour de la valeur limite).
/// </summary>
public sealed class AlertTracker
{
    private readonly HashSet<string> _active = [];

    public IReadOnlyCollection<string> Active => _active;

    public bool IsActive(string key) => _active.Contains(key);

    /// <summary>
    /// Évalue une mesure ; renvoie vrai uniquement au moment où l'alerte se déclenche.
    /// <paramref name="higherIsWorse"/> : vrai pour une température, faux pour un espace libre.
    /// Une mesure absente ne change rien.
    /// </summary>
    public bool Evaluate(string key, double? value, double threshold, double margin, bool higherIsWorse, bool enabled = true)
    {
        if (!enabled)
        {
            _active.Remove(key);
            return false;
        }
        if (value is not { } current || !double.IsFinite(current))
        {
            return false;
        }
        var beyond = higherIsWorse ? current >= threshold : current <= threshold;
        var recovered = higherIsWorse ? current < threshold - margin : current > threshold + margin;
        if (beyond)
        {
            return _active.Add(key);
        }
        if (recovered)
        {
            _active.Remove(key);
        }
        return false;
    }
}
