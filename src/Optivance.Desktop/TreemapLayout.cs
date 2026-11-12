using System.Windows;

namespace Optivance.Desktop;

internal readonly record struct TreemapTile(StorageRow Row, Rect Bounds);

internal static class TreemapLayoutEngine
{
    public static TreemapTile[] Calculate(IReadOnlyList<StorageRow> source, double width, double height, CancellationToken cancellationToken)
    {
        if (width <= 1 || height <= 1)
        {
            return [];
        }

        var items = source
            .Where(row => row.TreemapWeight > 0)
            .OrderByDescending(row => row.TreemapWeight)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (items.Length == 0)
        {
            return [];
        }

        var remainingWeight = items.Sum(item => (double)item.TreemapWeight);
        if (remainingWeight <= 0 || double.IsInfinity(remainingWeight))
        {
            return [];
        }

        var remaining = new Rect(0, 0, width, height);
        var tiles = new TreemapTile[items.Length];
        var index = 0;
        var written = 0;

        while (index < items.Length && remaining.Width > 0.01 && remaining.Height > 0.01)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowStart = index;
            var rowWeight = (double)items[index].TreemapWeight;
            var shortSide = Math.Min(remaining.Width, remaining.Height);
            var currentWorst = WorstAspect(items, rowStart, index + 1, rowWeight, shortSide, remaining.Width * remaining.Height, remainingWeight);
            index++;

            while (index < items.Length)
            {
                var candidateWeight = rowWeight + items[index].TreemapWeight;
                var candidateWorst = WorstAspect(items, rowStart, index + 1, candidateWeight, shortSide, remaining.Width * remaining.Height, remainingWeight);
                if (candidateWorst > currentWorst)
                {
                    break;
                }
                rowWeight = candidateWeight;
                currentWorst = candidateWorst;
                index++;
            }

            var scale = remaining.Width * remaining.Height / remainingWeight;
            var rowArea = rowWeight * scale;
            if (remaining.Width >= remaining.Height)
            {
                var stripWidth = Math.Min(remaining.Width, rowArea / remaining.Height);
                var y = remaining.Y;
                for (var itemIndex = rowStart; itemIndex < index; itemIndex++)
                {
                    var isLast = itemIndex == index - 1;
                    var tileHeight = isLast
                        ? remaining.Bottom - y
                        : Math.Min(remaining.Bottom - y, remaining.Height * items[itemIndex].TreemapWeight / rowWeight);
                    tiles[written++] = new TreemapTile(items[itemIndex], new Rect(remaining.X, y, stripWidth, Math.Max(0, tileHeight)));
                    y += tileHeight;
                }
                remaining = new Rect(remaining.X + stripWidth, remaining.Y, Math.Max(0, remaining.Width - stripWidth), remaining.Height);
            }
            else
            {
                var stripHeight = Math.Min(remaining.Height, rowArea / remaining.Width);
                var x = remaining.X;
                for (var itemIndex = rowStart; itemIndex < index; itemIndex++)
                {
                    var isLast = itemIndex == index - 1;
                    var tileWidth = isLast
                        ? remaining.Right - x
                        : Math.Min(remaining.Right - x, remaining.Width * items[itemIndex].TreemapWeight / rowWeight);
                    tiles[written++] = new TreemapTile(items[itemIndex], new Rect(x, remaining.Y, Math.Max(0, tileWidth), stripHeight));
                    x += tileWidth;
                }
                remaining = new Rect(remaining.X, remaining.Y + stripHeight, remaining.Width, Math.Max(0, remaining.Height - stripHeight));
            }
            remainingWeight = Math.Max(0, remainingWeight - rowWeight);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (written == tiles.Length)
        {
            return tiles;
        }
        return tiles.AsSpan(0, written).ToArray();
    }

    private static double WorstAspect(
        IReadOnlyList<StorageRow> items,
        int start,
        int end,
        double weight,
        double shortSide,
        double remainingArea,
        double remainingWeight)
    {
        if (weight <= 0 || shortSide <= 0 || remainingWeight <= 0)
        {
            return double.PositiveInfinity;
        }

        var maxArea = 0d;
        var minArea = double.PositiveInfinity;
        for (var index = start; index < end; index++)
        {
            var area = items[index].TreemapWeight * remainingArea / remainingWeight;
            maxArea = Math.Max(maxArea, area);
            minArea = Math.Min(minArea, area);
        }

        var rowArea = weight * remainingArea / remainingWeight;
        var sideSquared = shortSide * shortSide;
        return Math.Max(sideSquared * maxArea / (rowArea * rowArea), (rowArea * rowArea) / (sideSquared * minArea));
    }
}
