namespace Optivance.Core.Storage;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;
}

/// <summary>Disposition « squarified » (Bruls, Huizing, van Wijk) de poids triés par ordre décroissant.</summary>
public static class Squarify
{
    public static void Layout(ReadOnlySpan<double> weights, RectD bounds, Span<RectD> output)
    {
        var total = 0d;
        foreach (var weight in weights)
        {
            total += weight;
        }
        if (weights.Length == 0 || total <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            output[..weights.Length].Clear();
            return;
        }

        var remaining = bounds;
        var remainingWeight = total;
        var index = 0;
        while (index < weights.Length)
        {
            var shortSide = Math.Min(remaining.Width, remaining.Height);
            var scale = remaining.Area / remainingWeight;
            var rowStart = index;
            var rowWeight = weights[index];
            var worst = Worst(weights, rowStart, index + 1, rowWeight, shortSide, scale);
            index++;
            while (index < weights.Length)
            {
                var candidate = Worst(weights, rowStart, index + 1, rowWeight + weights[index], shortSide, scale);
                if (candidate > worst)
                {
                    break;
                }
                worst = candidate;
                rowWeight += weights[index];
                index++;
            }

            var isLastRow = index == weights.Length;
            if (remaining.Width >= remaining.Height)
            {
                var stripWidth = isLastRow ? remaining.Width : Math.Min(remaining.Width, rowWeight * scale / remaining.Height);
                var y = remaining.Y;
                for (var item = rowStart; item < index; item++)
                {
                    var height = item == index - 1 ? remaining.Bottom - y : remaining.Height * weights[item] / rowWeight;
                    output[item] = new RectD(remaining.X, y, stripWidth, Math.Max(0, height));
                    y += height;
                }
                remaining = new RectD(remaining.X + stripWidth, remaining.Y, Math.Max(0, remaining.Width - stripWidth), remaining.Height);
            }
            else
            {
                var stripHeight = isLastRow ? remaining.Height : Math.Min(remaining.Height, rowWeight * scale / remaining.Width);
                var x = remaining.X;
                for (var item = rowStart; item < index; item++)
                {
                    var width = item == index - 1 ? remaining.Right - x : remaining.Width * weights[item] / rowWeight;
                    output[item] = new RectD(x, remaining.Y, Math.Max(0, width), stripHeight);
                    x += width;
                }
                remaining = new RectD(remaining.X, remaining.Y + stripHeight, remaining.Width, Math.Max(0, remaining.Height - stripHeight));
            }
            remainingWeight -= rowWeight;
            if (remainingWeight <= 0)
            {
                for (var item = index; item < weights.Length; item++)
                {
                    output[item] = default;
                }
                return;
            }
        }
    }

    private static double Worst(ReadOnlySpan<double> weights, int start, int end, double rowWeight, double shortSide, double scale)
    {
        var rowArea = rowWeight * scale;
        if (rowArea <= 0 || shortSide <= 0)
        {
            return double.PositiveInfinity;
        }
        var maxArea = weights[start] * scale;
        var minArea = weights[end - 1] * scale;
        var side2 = shortSide * shortSide;
        var row2 = rowArea * rowArea;
        return Math.Max(side2 * maxArea / row2, row2 / (side2 * Math.Max(minArea, double.Epsilon)));
    }
}

/// <summary>Image calculée : pixels BGRA32, propriétaire de chaque pixel, rectangles des éléments dessinés.</summary>
public sealed class TreemapFrame(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int[] Pixels { get; } = new int[width * height];
    /// <summary>Identifiant de l'élément visible à chaque pixel (-1 : aucun).</summary>
    public int[] Owners { get; } = CreateOwners(width * height);
    public Dictionary<int, RectD> Bounds { get; } = [];

    public int HitTest(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? -1 : Owners[y * Width + x];

    private static int[] CreateOwners(int length)
    {
        var owners = new int[length];
        Array.Fill(owners, -1);
        return owners;
    }
}

/// <summary>
/// Treemap imbriquée à coussins (van Wijk & van de Wetering) : chaque niveau ajoute un relief,
/// les fichiers sont colorés par extension. Calcul pur (aucune dépendance graphique).
/// </summary>
public static class CushionTreemap
{
    private const double InitialHeight = 0.5;
    private const double HeightFactor = 0.75;
    private const double Ambient = 0.30;
    private const double Diffuse = 0.80;
    // Lumière venant du haut à gauche.
    private static readonly double LightX = -0.09759, LightY = -0.19518, LightZ = 0.97590;

    /// <param name="colorOf">Couleur RGB (0xRRGGBB) d'un fichier ; les dossiers trop petits pour être détaillés sont gris.</param>
    public static TreemapFrame Render(StorageTree tree, int rootId, int width, int height, Func<int, int> colorOf, CancellationToken cancellationToken)
    {
        var frame = new TreemapFrame(Math.Max(1, width), Math.Max(1, height));
        Array.Fill(frame.Pixels, unchecked((int)0xFF14171E));
        if (tree.Weight(rootId) == 0 || width < 2 || height < 2)
        {
            return frame;
        }
        var surface = new double[4];
        Node(tree, rootId, new RectD(0, 0, width, height), InitialHeight, surface, frame, colorOf, cancellationToken);
        return frame;
    }

    private static void Node(StorageTree tree, int id, RectD rect, double ridge, double[] parentSurface, TreemapFrame frame, Func<int, int> colorOf, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        frame.Bounds[id] = rect;
        var surface = (double[])parentSurface.Clone();
        AddRidge(rect.X, rect.Right, ridge, ref surface[0], ref surface[1]);
        AddRidge(rect.Y, rect.Bottom, ridge, ref surface[2], ref surface[3]);

        if (tree.IsDirectory(id) && rect.Width >= 3 && rect.Height >= 3)
        {
            var children = new List<int>();
            var weights = new List<double>();
            foreach (var child in tree.ChildrenBySize(id))
            {
                var weight = tree.Weight(child);
                if (weight == 0)
                {
                    break;
                }
                children.Add(child);
                weights.Add(weight);
            }
            if (children.Count > 0)
            {
                // Réserve fine pour lire les bords des dossiers.
                var inner = rect.Width > 8 && rect.Height > 8 ? new RectD(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2) : rect;
                var rects = new RectD[children.Count];
                Squarify.Layout(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(weights), inner, rects);
                var drawnArea = 0d;
                for (var index = 0; index < children.Count; index++)
                {
                    if (rects[index].Area < 1.0)
                    {
                        continue;
                    }
                    drawnArea += rects[index].Area;
                    Node(tree, children[index], rects[index], ridge * HeightFactor, surface, frame, colorOf, cancellationToken);
                }
                if (drawnArea > 0)
                {
                    // Le cadre (et la poussière d'éléments minuscules) reste au dossier.
                    Fill(frame, rect, surface, 0x3A4150, id, onlyEmpty: true);
                    return;
                }
            }
        }
        var color = tree.IsDirectory(id) ? 0x5A6272 : colorOf(id);
        Fill(frame, rect, surface, color, id, onlyEmpty: false);
    }

    private static void AddRidge(double x1, double x2, double height, ref double s1, ref double s2)
    {
        var width = x2 - x1;
        if (width <= 0)
        {
            return;
        }
        s1 += 4 * height * (x2 + x1) / width;
        s2 -= 4 * height / width;
    }

    private static void Fill(TreemapFrame frame, RectD rect, double[] surface, int rgb, int owner, bool onlyEmpty)
    {
        var x0 = Math.Clamp((int)Math.Round(rect.X), 0, frame.Width);
        var x1 = Math.Clamp((int)Math.Round(rect.Right), 0, frame.Width);
        var y0 = Math.Clamp((int)Math.Round(rect.Y), 0, frame.Height);
        var y1 = Math.Clamp((int)Math.Round(rect.Bottom), 0, frame.Height);
        double red = (rgb >> 16) & 0xFF, green = (rgb >> 8) & 0xFF, blue = rgb & 0xFF;
        for (var y = y0; y < y1; y++)
        {
            var ny = -(2 * surface[3] * (y + 0.5) + surface[2]);
            var row = y * frame.Width;
            for (var x = x0; x < x1; x++)
            {
                var pixel = row + x;
                if (onlyEmpty && frame.Owners[pixel] != -1)
                {
                    continue;
                }
                var nx = -(2 * surface[1] * (x + 0.5) + surface[0]);
                var cosine = (nx * LightX + ny * LightY + LightZ) / Math.Sqrt(nx * nx + ny * ny + 1.0);
                var intensity = Ambient + Diffuse * Math.Max(0, cosine);
                var r = (int)Math.Min(255, red * intensity);
                var g = (int)Math.Min(255, green * intensity);
                var b = (int)Math.Min(255, blue * intensity);
                frame.Pixels[pixel] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                frame.Owners[pixel] = owner;
            }
        }
    }
}
