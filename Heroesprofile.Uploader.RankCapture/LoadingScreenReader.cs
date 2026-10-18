using System;
using System.Collections.Generic;
using System.Linq;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Reads the ten players' Storm League ranks off the loading screen's card strips (the left and right
    /// quarter of the game window, as <see cref="GameCapture"/> keeps them). Worked out from players'
    /// loading-screen samples (2026-10); each step, per card:
    /// <list type="number">
    /// <item>Master or Grandmaster: the portrait frame matches one of theirs (Grandmaster's has lightning around it and the ladder position on it).</item>
    /// <item>Otherwise no division badge (the shield in the portrait's corner) means unranked - the frame is then just the player's chosen border, which can look like any league's or carry their account level.</item>
    /// <item>Otherwise the league is the frame's colour, and the division the badge's digit.</item>
    /// </list>
    /// Everything is measured in card units: 1 unit = window height / 1440 pixels, since the cards scale with
    /// the window's height and sit against its left and right edges whatever its width.
    /// </summary>
    internal static class LoadingScreenReader
    {
        // A card, in card units: 414 x 164, its top-left corner 8 units in from its edge of the screen and
        // 248 + 176 * slot units down.
        private const double CardWidth = 414;
        private const double CardInset = 8;
        private const double FirstCardTop = 248;
        private const double CardPitch = 176;
        public const int Slots = 5;

        // The portrait square (frame included) on a left card; a right card mirrors it.
        private const int PortraitX = 14, PortraitY = 18, PortraitSize = 132;

        // The division badge, sampled at 2 pixels per unit: the shield sits at the portrait's bottom-left
        // corner on both sides.
        private const int BadgeLeftX = 5, BadgeRightX = 255, BadgeY = 85, BadgeWidth = 140, BadgeHeight = 150;

        // Midway across the gaps in the samples (2,800 cards, 2026-10-18): Master/Grandmaster frames matched
        // 0.82 and up, every other card 0.67 at most; badges' shields 0.54 and up, unranked cards 0.35 at most.
        public const double MasterCutoff = 0.75;
        public const double ShieldCutoff = 0.45;
        public const int DigitWidth = 20, DigitHeight = 28;

        /// <summary>
        /// The ten cards' ranks from one frame, or null when the frame isn't the loading screen (most of its
        /// cards aren't where they should be: the game was minimised, in a menu, or already in the match).
        /// </summary>
        public static IReadOnlyList<CardRank> ReadFrame(Pixels left, Pixels right, RankTemplates templates)
        {
            if (!IsLoadingScreen(left, right)) {
                return null;
            }
            var ranks = new List<CardRank>();
            foreach (var side in new[] { Side.Left, Side.Right }) {
                for (var slot = 0; slot < Slots; slot++) {
                    ranks.Add(ReadCard(side == Side.Left ? left : right, side, slot, templates));
                }
            }
            return ranks;
        }

        /// <summary>
        /// The ten cards' ranks from several frames of one loading screen, each card taking what most frames
        /// agree on (null when no frame shows the loading screen). A card counts as unranked only when no two
        /// frames agree on a rank for it.
        /// </summary>
        public static IReadOnlyList<CardRank> ReadGame(IEnumerable<(Pixels Left, Pixels Right)> frames, RankTemplates templates) =>
            Vote(frames.Select(f => ReadFrame(f.Left, f.Right, templates)).Where(r => r != null).ToList());

        /// <summary><see cref="ReadGame"/>'s vote over the frames' reads.</summary>
        public static IReadOnlyList<CardRank> Vote(IReadOnlyList<IReadOnlyList<CardRank>> reads)
        {
            if (reads.Count == 0) {
                return null;
            }
            var result = new List<CardRank>();
            for (var i = 0; i < reads[0].Count; i++) {
                var card = reads.Select(r => r[i]).ToList();
                var ranked = card.Where(c => c.League != League.Unranked)
                    .GroupBy(c => (c.League, c.Division))
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();
                var minimum = Math.Min(2, reads.Count);
                result.Add(ranked != null && ranked.Count() >= minimum ? ranked.First() : card.First() with { League = League.Unranked, Division = null });
            }
            return result;
        }

        public static CardRank ReadCard(Pixels strip, Side side, int slot, RankTemplates templates)
        {
            var features = Measure(strip, side, slot, templates);
            return Classify(side, slot, features);
        }

        /// <summary>Everything the decision looks at for one card, for <see cref="Classify"/> and for tuning.</summary>
        public sealed record CardFeatures(double Master, double Grandmaster, double Shield, FrameColour Colour, int? Digit, double DigitScore);

        public sealed record FrameColour(float R, float G, float B, float Hue, float Saturation);

        public static CardFeatures Measure(Pixels strip, Side side, int slot, RankTemplates templates)
        {
            var portrait = Portrait(strip, side, slot);
            var ring = FrameRing(portrait);
            var badge = Badge(strip, side, slot);
            var (digit, digitScore) = ReadDigit(DigitGlyph(badge), templates.Digits);
            return new CardFeatures(
                Dot(ring, templates.Master),
                Dot(ring, templates.Grandmaster),
                ShieldScore(ShieldEdges(badge), templates.Shield),
                MeasureFrameColour(portrait),
                digit,
                digitScore);
        }

        public static CardRank Classify(Side side, int slot, CardFeatures f)
        {
            if (Math.Max(f.Master, f.Grandmaster) >= MasterCutoff) {
                return new CardRank(side, slot, f.Grandmaster > f.Master ? League.Grandmaster : League.Master, null);
            }
            if (f.Shield < ShieldCutoff) {
                return new CardRank(side, slot, League.Unranked, null);
            }
            return new CardRank(side, slot, LeagueFromColour(f.Colour), f.Digit);
        }

        /// <summary>
        /// The league a badge-carrying card's frame colour says. Metal-coloured frames (saturated) by hue:
        /// Bronze copper, Gold yellow, Diamond cyan. Pale ones by tint: Silver neutral grey, Platinum pink.
        /// </summary>
        public static League LeagueFromColour(FrameColour c)
        {
            if (c.Saturation >= 60) {
                if (c.Hue < 30) {
                    return League.Bronze;
                }
                if (c.Hue < 60) {
                    return League.Gold;
                }
                if (c.Hue >= 150 && c.Hue < 230) {
                    return League.Diamond;
                }
                return League.Unknown;
            }
            var redOverGreen = c.R - c.G;
            var blueOverRed = c.B - c.R;
            if (redOverGreen <= 2 && blueOverRed >= -15 && blueOverRed <= 40) {
                return League.Silver;
            }
            if (redOverGreen >= 12 && redOverGreen <= 45 && blueOverRed <= 10) {
                return League.Platinum;
            }
            return League.Unknown;
        }

        // --- Where things are ---

        private static double Unit(Pixels strip) => strip.Height / 1440.0;

        /// <summary>The card's top-left corner in the strip, in pixels.</summary>
        private static (double X, double Y) CardOrigin(Pixels strip, Side side, int slot)
        {
            var unit = Unit(strip);
            var x = side == Side.Left ? CardInset * unit : strip.Width - (CardInset + CardWidth) * unit;
            return (x, (FirstCardTop + CardPitch * slot) * unit);
        }

        /// <summary>Part of a card, at <paramref name="perUnit"/> pixels per card unit.</summary>
        public static Pixels SampleCardArea(Pixels strip, Side side, int slot, double x, double y, int width, int height, int perUnit = 1)
        {
            var unit = Unit(strip);
            var (cx, cy) = CardOrigin(strip, side, slot);
            return strip.Sample(cx + x * unit, cy + y * unit, unit / perUnit, width, height);
        }

        /// <summary>The portrait square, 132 x 132, mirrored on right cards so its frame always reads the same way.</summary>
        public static Pixels Portrait(Pixels strip, Side side, int slot)
        {
            var x = side == Side.Left ? PortraitX : CardWidth - PortraitX - PortraitSize;
            var portrait = SampleCardArea(strip, side, slot, x, PortraitY, PortraitSize, PortraitSize);
            return side == Side.Left ? portrait : portrait.FlipHorizontal();
        }

        /// <summary>The division badge's area at 2 pixels per unit (140 x 150), not mirrored.</summary>
        public static Pixels Badge(Pixels strip, Side side, int slot) =>
            SampleCardArea(strip, side, slot, side == Side.Left ? BadgeLeftX : BadgeRightX, BadgeY, BadgeWidth, BadgeHeight, perUnit: 2);

        // --- Is this the loading screen? ---

        /// <summary>
        /// Whether at least 9 of the 10 cards show the bright coloured line along their outer edge. Frames of
        /// the menu, the match itself or a minimised window don't.
        /// </summary>
        public static bool IsLoadingScreen(Pixels left, Pixels right)
        {
            if (left.Height < 480 || right.Height < 480) {
                return false;
            }
            var found = 0;
            foreach (var side in new[] { Side.Left, Side.Right }) {
                for (var slot = 0; slot < Slots; slot++) {
                    if (HasCardEdge(side == Side.Left ? left : right, side, slot)) {
                        found++;
                    }
                }
            }
            return found >= 9;
        }

        private static bool HasCardEdge(Pixels strip, Side side, int slot)
        {
            // The card's outer edge runs down the first few units of its outside; look a little either side.
            var x = side == Side.Left ? -4 : CardWidth - 22;
            var area = SampleCardArea(strip, side, slot, x, 22, 26, 120);
            var column = new float[area.Height];
            for (var cx = 0; cx < area.Width; cx++) {
                for (var cy = 0; cy < area.Height; cy++) {
                    var (_, s, v) = area.Hsv(cx, cy);
                    column[cy] = s > 90 ? v : 0;
                }
                if (Percentile(column, 50) > 120) {
                    return true;
                }
            }
            return false;
        }

        // --- Master and Grandmaster: the portrait frame itself ---

        /// <summary>
        /// The portrait's frame (everything outside its inner 96 x 96, but not the badge corner), as one
        /// normalised vector: comparing two is a dot product.
        /// </summary>
        public static float[] FrameRing(Pixels portrait)
        {
            var values = new List<float>();
            for (var y = 0; y < PortraitSize; y++) {
                for (var x = 0; x < PortraitSize; x++) {
                    var inside = y >= 18 && y < 114 && x >= 18 && x < 114;
                    var badgeCorner = y >= 95 && x < 50;
                    if (inside || badgeCorner) {
                        continue;
                    }
                    values.Add(portrait.R(x, y));
                    values.Add(portrait.G(x, y));
                    values.Add(portrait.B(x, y));
                }
            }
            return Normalise(values.ToArray());
        }

        // --- The division badge ---

        private const int ShieldTop = 10, ShieldLeft = 30, ShieldHeight = 105, ShieldWidth = 90;

        /// <summary>
        /// Edge strength across the badge area (105 x 90 of it), where the shield's outline is. The digit's
        /// own edges are left out (zeroed), so the shield matches whatever its number.
        /// </summary>
        public static float[] ShieldEdges(Pixels badge)
        {
            var gray = badge.GrayValues();
            float G(int x, int y) => gray[(ShieldTop + y) * badge.Width + ShieldLeft + x];
            var edges = new float[ShieldHeight * ShieldWidth];
            for (var y = 0; y < ShieldHeight; y++) {
                for (var x = 0; x < ShieldWidth; x++) {
                    // Central differences inside, one-sided at the borders (as numpy.gradient).
                    var gx = x == 0 ? G(1, y) - G(0, y) : x == ShieldWidth - 1 ? G(x, y) - G(x - 1, y) : (G(x + 1, y) - G(x - 1, y)) / 2;
                    var gy = y == 0 ? G(x, 1) - G(x, 0) : y == ShieldHeight - 1 ? G(x, y) - G(x, y - 1) : (G(x, y + 1) - G(x, y - 1)) / 2;
                    edges[y * ShieldWidth + x] = MathF.Sqrt(gx * gx + gy * gy);
                }
            }
            return edges;
        }

        private static bool InShieldMask(int x, int y) => !(y >= 22 && y < 70 && x >= 28 && x < 66);

        /// <summary>How well the badge area's edges match the shield's, allowing it to sit a few pixels off.</summary>
        public static double ShieldScore(float[] edges, float[] template)
        {
            // The template side, normalised over the mask; it's the same for every offset.
            var positions = new List<int>();
            for (var y = 0; y < ShieldHeight; y++) {
                for (var x = 0; x < ShieldWidth; x++) {
                    if (InShieldMask(x, y)) {
                        positions.Add(y * ShieldWidth + x);
                    }
                }
            }
            var t = Normalise(positions.Select(p => template[p]).ToArray());

            var best = -1.0;
            for (var dy = -4; dy <= 4; dy += 2) {
                for (var dx = -4; dx <= 4; dx += 2) {
                    // Correlation with the edges moved by (dx, dy), wrapping round: t has mean 0 and length 1,
                    // so it's sum(a * t) over the length of a's deviations.
                    double sum = 0, sumSquares = 0, cross = 0;
                    for (var i = 0; i < positions.Count; i++) {
                        var y = positions[i] / ShieldWidth;
                        var x = positions[i] % ShieldWidth;
                        var a = edges[Mod(y - dy, ShieldHeight) * ShieldWidth + Mod(x - dx, ShieldWidth)];
                        sum += a;
                        sumSquares += (double)a * a;
                        cross += a * t[i];
                    }
                    var spread = Math.Sqrt(Math.Max(0, sumSquares - sum * sum / positions.Count));
                    best = Math.Max(best, cross / (spread + 1e-6));
                }
            }
            return best;
        }

        /// <summary>
        /// The badge's digit, scaled to 20 x 28 and normalised, or null when nothing digit-shaped is there.
        /// The digit is the brightest thing inside the shield: a blob of digit size near its middle, picked
        /// out with a threshold set from the shield's own inside (a bright portrait next to it doesn't move it).
        /// </summary>
        public static float[] DigitGlyph(Pixels badge)
        {
            var gray = badge.GrayValues();
            int w = badge.Width;
            const int x0 = 48, x1 = 110, y0 = 30, y1 = 88;
            var window = new List<float>();
            for (var y = y0; y < y1; y++) {
                for (var x = x0; x < x1; x++) {
                    window.Add(gray[y * w + x]);
                }
            }
            var values = window.ToArray();
            var threshold = (Percentile(values, 25) + Percentile(values, 99)) / 2;

            var labels = new int[gray.Length];
            var best = (Score: double.MinValue, Top: 0, Bottom: 0, CentreX: 0.0);
            var next = 0;
            for (var y = y0; y < y1; y++) {
                for (var x = x0; x < x1; x++) {
                    if (labels[y * w + x] != 0 || gray[y * w + x] <= threshold) {
                        continue;
                    }
                    // Flood one blob (4-connected), keeping its bounding box and size.
                    next++;
                    int top = y, bottom = y, left = x, right = x, area = 0;
                    var queue = new Queue<(int X, int Y)>();
                    queue.Enqueue((x, y));
                    labels[y * w + x] = next;
                    while (queue.Count > 0) {
                        var (px, py) = queue.Dequeue();
                        area++;
                        top = Math.Min(top, py);
                        bottom = Math.Max(bottom, py);
                        left = Math.Min(left, px);
                        right = Math.Max(right, px);
                        foreach (var (nx, ny) in new[] { (px + 1, py), (px - 1, py), (px, py + 1), (px, py - 1) }) {
                            if (nx < x0 || nx >= x1 || ny < y0 || ny >= y1 || labels[ny * w + nx] != 0 || gray[ny * w + nx] <= threshold) {
                                continue;
                            }
                            labels[ny * w + nx] = next;
                            queue.Enqueue((nx, ny));
                        }
                    }
                    var height = bottom - top + 1;
                    var width = right - left + 1;
                    if (height < 20 || height > 46 || width < 4 || width > 34 || width > height) {
                        continue;
                    }
                    var cy = (top + bottom + 1) / 2.0;
                    var cx = (left + right + 1) / 2.0;
                    if (cx < 55 || cx > 102 || cy < 38 || cy > 78) {
                        continue;
                    }
                    var score = area - Math.Abs(cy - 58) * 3;
                    if (score > best.Score) {
                        best = (score, top, bottom + 1, cx);
                    }
                }
            }
            if (best.Score == double.MinValue) {
                return null;
            }

            // A box of fixed proportions around the blob, so a narrow 1 keeps its shape.
            var boxHeight = best.Bottom - best.Top;
            var boxLeft = (int)(best.CentreX - boxHeight * 0.36);
            var boxRight = (int)(best.CentreX + boxHeight * 0.36);
            var glyph = badge.Sample(boxLeft, best.Top, (boxRight - boxLeft) / (double)DigitWidth, boxHeight / (double)DigitHeight, DigitWidth, DigitHeight);
            var g = glyph.GrayValues();
            var mean = g.Average();
            var sd = (float)Math.Sqrt(g.Select(v => (v - mean) * (v - mean)).Average());
            return g.Select(v => (v - mean) / (sd + 1e-6f)).ToArray();
        }

        /// <summary>The template digit (1-5) <paramref name="glyph"/> matches best, and how well.</summary>
        public static (int? Digit, double Score) ReadDigit(float[] glyph, float[][] templates)
        {
            if (glyph == null) {
                return (null, 0);
            }
            int? digit = null;
            var best = double.MinValue;
            for (var i = 0; i < templates.Length; i++) {
                var t = templates[i];
                var mean = t.Average();
                var sd = Math.Sqrt(t.Select(v => (v - mean) * (v - mean)).Average());
                var score = glyph.Zip(t, (a, b) => (double)a * b).Average() / (sd + 1e-6);
                if (score > best) {
                    best = score;
                    digit = i + 1;
                }
            }
            return (digit, best);
        }

        // --- The league: the frame's colour ---

        /// <summary>
        /// The frame's colour, measured only on its bars - the top one and the upper part of both sides - and
        /// only their brighter half, the metal itself. A looser area takes in the card's background (fiery
        /// hero art made Silver frames read as Gold).
        /// </summary>
        public static FrameColour MeasureFrameColour(Pixels portrait)
        {
            var pixels = new List<(float R, float G, float B, float H, float S, float V)>();
            void Bar(int xa, int xb, int ya, int yb)
            {
                for (var y = ya; y < yb; y++) {
                    for (var x = xa; x < xb; x++) {
                        var (h, s, v) = portrait.Hsv(x, y);
                        pixels.Add((portrait.R(x, y), portrait.G(x, y), portrait.B(x, y), h, s, v));
                    }
                }
            }
            Bar(24, 108, 10, 17);
            Bar(11, 18, 24, 80);
            Bar(106, 113, 24, 80);

            var medianValue = Percentile(pixels.Select(p => p.V).ToArray(), 50);
            var bright = pixels.Where(p => p.V >= medianValue).ToList();
            return new FrameColour(
                bright.Average(p => p.R),
                bright.Average(p => p.G),
                bright.Average(p => p.B),
                Percentile(bright.Select(p => p.H).ToArray(), 50),
                Percentile(bright.Select(p => p.S).ToArray(), 50));
        }

        // --- Small maths ---

        /// <summary>Linear-interpolated percentile (numpy's default).</summary>
        public static float Percentile(float[] values, double percent)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted);
            var position = percent / 100 * (sorted.Length - 1);
            var lo = (int)Math.Floor(position);
            var hi = Math.Min(lo + 1, sorted.Length - 1);
            return (float)(sorted[lo] + (sorted[hi] - sorted[lo]) * (position - lo));
        }

        /// <summary>Mean taken away and scaled to length 1.</summary>
        public static float[] Normalise(float[] values)
        {
            var mean = values.Average();
            var centred = values.Select(v => v - mean).ToArray();
            var length = (float)Math.Sqrt(centred.Sum(v => (double)v * v));
            return centred.Select(v => v / (length + 1e-6f)).ToArray();
        }

        public static double Dot(float[] a, float[] b)
        {
            double sum = 0;
            for (var i = 0; i < a.Length; i++) {
                sum += (double)a[i] * b[i];
            }
            return sum;
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;
    }
}
