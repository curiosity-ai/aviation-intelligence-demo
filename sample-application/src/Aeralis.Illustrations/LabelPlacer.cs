using SkiaSharp;

namespace Aeralis.Illustrations;

/// <summary>Where a callout's label ended up, and the leader that reaches it.</summary>
internal sealed record Placement(SKPoint Elbow, SKPoint ShelfEnd, SKRect Text, bool Clean);

/// <summary>
/// Decides where callout labels go, by measuring the free space on the page rather than
/// guessing at a few fixed positions.
/// </summary>
/// <remarks>
/// <para>
/// Everything already drawn — the aircraft, the dimension lines and their figures — is
/// rasterised into a binary mask. Enclosed white areas are then flooded shut, so the inside of
/// a fuselage counts as solid rather than as somewhere a label could sit, and the whole mask is
/// dilated to leave a little air around the artwork.
/// </para>
/// <para>
/// A distance transform over what is left gives every free cell its distance to the nearest
/// obstacle: zero along the edges of the no-go regions, rising to a ridge along the middle of
/// each open corridor. That surface is what makes a good label position measurable — a label
/// centred on the ridge of a corridor is as far from the artwork as that corridor allows.
/// </para>
/// <para>
/// For each callout the placer fans rays out from the arrow tip and takes, per direction, the
/// first radius at which the whole text box lands in free space. Candidates are scored on
/// leader length, with clearance breaking ties, so labels sit close to what they point at but
/// still in open space. Each label is added to the mask once placed, so later ones flow around
/// it. If nothing fits anywhere — a crowded page, a very long label — the placer returns the
/// position with the least overlap rather than refusing to draw.
/// </para>
/// </remarks>
internal sealed class LabelPlacer
{
    /// <summary>Cells across the longer side of the page.</summary>
    private const int Resolution = 420;

    /// <summary>Air left around the artwork, in cells.</summary>
    private const int Dilation = 3;

    private const int Directions = 64;

    /// <summary>Weight on clearance when scoring; small, so a short leader still wins.</summary>
    private const float ClearanceWeight = 0.35f;

    private readonly SKRect _bounds;
    private readonly float _cell;
    private readonly int _width;
    private readonly int _height;

    private bool[,] _occupied;
    private int[,] _sums;
    private float[,] _distance;

    private LabelPlacer(SKRect bounds, float cell, int width, int height)
    {
        _bounds = bounds;
        _cell = cell;
        _width = width;
        _height = height;
        _occupied = new bool[width, height];
        _sums = new int[width + 1, height + 1];
        _distance = new float[width, height];
    }

    /// <param name="bounds">The area labels may occupy.</param>
    /// <param name="drawObstacles">
    /// Draws everything a label must avoid, in page coordinates — the same calls that put it on
    /// the visible canvas.
    /// </param>
    public static LabelPlacer Build(SKRect bounds, Action<SKCanvas> drawObstacles)
    {
        var cell = Math.Max(bounds.Width, bounds.Height) / Resolution;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width / cell));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height / cell));

        var placer = new LabelPlacer(bounds, cell, width, height);
        placer.Rasterise(drawObstacles);
        placer.FillEnclosed();
        placer.Dilate(Dilation);
        placer.Rebuild();
        return placer;
    }

    /// <summary>
    /// Finds a spot for a text box of the given size, with its leader starting at
    /// <paramref name="target"/>.
    /// </summary>
    /// <param name="shelf">Length of the horizontal run between the leader and the text.</param>
    public Placement Place(SKPoint target, float textWidth, float textHeight, float shelf)
    {
        var minRadius = _cell * 6;
        var maxRadius = MathF.Sqrt(_bounds.Width * _bounds.Width + _bounds.Height * _bounds.Height);
        var step = _cell * 1.5f;

        Placement? best = null;
        var bestScore = float.MaxValue;

        Placement? leastBad = null;
        var leastOverlap = float.MaxValue;

        for (var d = 0; d < Directions; d++)
        {
            var angle = d * 2 * MathF.PI / Directions;
            float ux = MathF.Cos(angle), uy = MathF.Sin(angle);

            for (var r = minRadius; r <= maxRadius; r += step)
            {
                var candidate = Compose(target, ux, uy, r, textWidth, textHeight, shelf);
                if (!Inside(candidate.Text)) continue;

                var overlap = OccupiedCells(candidate.Text);
                if (overlap > 0)
                {
                    if (overlap < leastOverlap) { leastOverlap = overlap; leastBad = candidate; }
                    continue;
                }

                var score = r - ClearanceWeight * Clearance(candidate.Text);
                if (score < bestScore) { bestScore = score; best = candidate with { Clean = true }; }
                break;
            }
        }

        var placement = best ?? leastBad ?? Compose(target, 0, -1, minRadius, textWidth, textHeight, shelf);
        Occupy(placement.Text);
        return placement;
    }

    /// <summary>Builds the leader, shelf and text box for one direction and distance.</summary>
    private static Placement Compose(SKPoint target, float ux, float uy, float radius, float textWidth, float textHeight, float shelf)
    {
        var elbow = new SKPoint(target.X + ux * radius, target.Y + uy * radius);
        var toRight = ux >= 0;
        var shelfEnd = new SKPoint(elbow.X + (toRight ? shelf : -shelf), elbow.Y);

        // The text sits on the shelf, running away from the leader.
        var left = toRight ? shelfEnd.X : shelfEnd.X - textWidth;
        var text = new SKRect(left, shelfEnd.Y - textHeight, left + textWidth, shelfEnd.Y);

        return new Placement(elbow, shelfEnd, text, false);
    }

    /// <summary>Marks an area as taken, so later labels avoid it.</summary>
    public void Occupy(SKRect rect)
    {
        var (x1, y1, x2, y2) = Cells(Pad(rect, _cell));

        for (var x = x1; x <= x2; x++)
            for (var y = y1; y <= y2; y++)
                _occupied[x, y] = true;

        Rebuild();
    }

    private bool Inside(SKRect rect) =>
        rect.Left >= _bounds.Left && rect.Right <= _bounds.Right &&
        rect.Top >= _bounds.Top && rect.Bottom <= _bounds.Bottom;

    /// <summary>Occupied cell count under a rectangle, from the summed-area table.</summary>
    private int OccupiedCells(SKRect rect)
    {
        var (x1, y1, x2, y2) = Cells(Pad(rect, _cell));
        return _sums[x2 + 1, y2 + 1] - _sums[x1, y2 + 1] - _sums[x2 + 1, y1] + _sums[x1, y1];
    }

    /// <summary>Distance from the middle of a rectangle to the nearest obstacle, in pixels.</summary>
    private float Clearance(SKRect rect)
    {
        var x = Math.Clamp((int)((rect.MidX - _bounds.Left) / _cell), 0, _width - 1);
        var y = Math.Clamp((int)((rect.MidY - _bounds.Top) / _cell), 0, _height - 1);
        return _distance[x, y] * _cell;
    }

    private static SKRect Pad(SKRect rect, float pad) =>
        new(rect.Left - pad, rect.Top - pad, rect.Right + pad, rect.Bottom + pad);

    private (int X1, int Y1, int X2, int Y2) Cells(SKRect rect) =>
    (
        Math.Clamp((int)((rect.Left - _bounds.Left) / _cell), 0, _width - 1),
        Math.Clamp((int)((rect.Top - _bounds.Top) / _cell), 0, _height - 1),
        Math.Clamp((int)Math.Ceiling((rect.Right - _bounds.Left) / _cell), 0, _width - 1),
        Math.Clamp((int)Math.Ceiling((rect.Bottom - _bounds.Top) / _cell), 0, _height - 1)
    );

    /// <summary>
    /// Draws the obstacles into a bitmap and folds it down to the mask. The raster is several
    /// times finer than the mask because the artwork is hairlines, which anti-alias away to
    /// nothing if rendered straight at mask resolution.
    /// </summary>
    private void Rasterise(Action<SKCanvas> drawObstacles)
    {
        const int supersample = 3;
        int rw = _width * supersample, rh = _height * supersample;

        using var bitmap = new SKBitmap(rw, rh, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale(supersample / _cell);
            canvas.Translate(-_bounds.Left, -_bounds.Top);
            drawObstacles(canvas);
        }

        for (var y = 0; y < rh; y++)
            for (var x = 0; x < rw; x++)
                if (bitmap.GetPixel(x, y).Red < 250)
                    _occupied[x / supersample, y / supersample] = true;
    }

    /// <summary>
    /// Closes white areas with no route to the edge of the page — the inside of a fuselage, the
    /// gap between a wing and a nacelle — so labels are not placed in pockets of the artwork.
    /// </summary>
    private void FillEnclosed()
    {
        var reachable = new bool[_width, _height];
        var queue = new Queue<(int X, int Y)>();

        void Consider(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _width || y >= _height) return;
            if (_occupied[x, y] || reachable[x, y]) return;
            reachable[x, y] = true;
            queue.Enqueue((x, y));
        }

        for (var x = 0; x < _width; x++) { Consider(x, 0); Consider(x, _height - 1); }
        for (var y = 0; y < _height; y++) { Consider(0, y); Consider(_width - 1, y); }

        while (queue.TryDequeue(out var cell))
        {
            Consider(cell.X - 1, cell.Y);
            Consider(cell.X + 1, cell.Y);
            Consider(cell.X, cell.Y - 1);
            Consider(cell.X, cell.Y + 1);
        }

        for (var x = 0; x < _width; x++)
            for (var y = 0; y < _height; y++)
                if (!reachable[x, y])
                    _occupied[x, y] = true;
    }

    private void Dilate(int radius)
    {
        for (var pass = 0; pass < radius; pass++)
        {
            var grown = (bool[,])_occupied.Clone();

            for (var x = 0; x < _width; x++)
                for (var y = 0; y < _height; y++)
                {
                    if (_occupied[x, y]) continue;
                    if ((x > 0 && _occupied[x - 1, y]) || (x < _width - 1 && _occupied[x + 1, y]) ||
                        (y > 0 && _occupied[x, y - 1]) || (y < _height - 1 && _occupied[x, y + 1]))
                        grown[x, y] = true;
                }

            _occupied = grown;
        }
    }

    /// <summary>Refreshes the summed-area table and the distance surface after the mask changes.</summary>
    private void Rebuild()
    {
        _sums = new int[_width + 1, _height + 1];
        for (var x = 0; x < _width; x++)
            for (var y = 0; y < _height; y++)
                _sums[x + 1, y + 1] = (_occupied[x, y] ? 1 : 0) + _sums[x, y + 1] + _sums[x + 1, y] - _sums[x, y];

        _distance = DistanceTransform();
    }

    /// <summary>
    /// Chamfer distance to the nearest obstacle, in cells. Zero on the obstacles themselves and
    /// on the page edge, rising to a ridge along the middle of each free corridor.
    /// </summary>
    private float[,] DistanceTransform()
    {
        const float straight = 1f, diagonal = 1.41421356f;
        var distance = new float[_width, _height];

        for (var x = 0; x < _width; x++)
            for (var y = 0; y < _height; y++)
                distance[x, y] = _occupied[x, y] || x == 0 || y == 0 || x == _width - 1 || y == _height - 1
                    ? 0
                    : float.MaxValue / 4;

        for (var y = 0; y < _height; y++)
            for (var x = 0; x < _width; x++)
            {
                var d = distance[x, y];
                if (x > 0) d = Math.Min(d, distance[x - 1, y] + straight);
                if (y > 0) d = Math.Min(d, distance[x, y - 1] + straight);
                if (x > 0 && y > 0) d = Math.Min(d, distance[x - 1, y - 1] + diagonal);
                if (x < _width - 1 && y > 0) d = Math.Min(d, distance[x + 1, y - 1] + diagonal);
                distance[x, y] = d;
            }

        for (var y = _height - 1; y >= 0; y--)
            for (var x = _width - 1; x >= 0; x--)
            {
                var d = distance[x, y];
                if (x < _width - 1) d = Math.Min(d, distance[x + 1, y] + straight);
                if (y < _height - 1) d = Math.Min(d, distance[x, y + 1] + straight);
                if (x < _width - 1 && y < _height - 1) d = Math.Min(d, distance[x + 1, y + 1] + diagonal);
                if (x > 0 && y < _height - 1) d = Math.Min(d, distance[x - 1, y + 1] + diagonal);
                distance[x, y] = d;
            }

        return distance;
    }
}
