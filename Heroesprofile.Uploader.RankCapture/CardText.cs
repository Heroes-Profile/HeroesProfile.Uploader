using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// Reads text off a card: the player's name and their hero's, with the OS's own offline text recognition
    /// (<see cref="TextReaders"/>), and a Grandmaster's ladder position, mostly by matching its characters
    /// against templates. Readers only know the languages installed on the machine (and on Windows, one script
    /// each), so names are read with every one there is; <see cref="PlayerMatcher"/> keeps whichever reading
    /// matches a player. A name in a script no reader here knows comes back as junk or nothing, which the
    /// matcher copes with too.
    /// </summary>
    internal static class CardText
    {
        // Where the names sit, in card units (see LoadingScreenReader): the hero's, big and white, above the
        // player's, beside the portrait - after it on a left card, right-aligned before it on a right card.
        // Each line on its own: a sliver of the other line at its edge makes the reader give up on it.
        private const int NameTop = 110, NameHeight = 24;
        private const int HeroTop = 80, HeroHeight = 30;
        private const int TextLeftX = 142, TextRightX = 50, TextWidth = 220;

        // Names are read enlarged this much, with a white margin round them: without the margin Windows reads
        // nothing at all for many short names (tried on the samples, 2026-10-18).
        private const int Scale = 3;
        private const int Margin = 30;

        /// <summary>Whether this machine has any text reader.</summary>
        public static bool IsAvailable => TextReaders.All.Count > 0;

        /// <summary>The languages names are read in.</summary>
        public static IEnumerable<string> Languages => TextReaders.All.Select(r => r.Language);

        /// <summary>The card's player name as each installed reader reads it (empty readings left out).</summary>
        public static Task<IReadOnlyList<string>> ReadNameAsync(Pixels strip, Side side, int slot) => ReadLineAsync(strip, side, slot, NameTop, NameHeight);

        /// <summary>The card's hero name as each installed reader reads it (empty readings left out).</summary>
        public static Task<IReadOnlyList<string>> ReadHeroAsync(Pixels strip, Side side, int slot) => ReadLineAsync(strip, side, slot, HeroTop, HeroHeight);

        private static async Task<IReadOnlyList<string>> ReadLineAsync(Pixels strip, Side side, int slot, int top, int height)
        {
            var area = LoadingScreenReader.SampleCardArea(strip, side, slot, side == Side.Left ? TextLeftX : TextRightX, top, TextWidth * Scale, height * Scale, Scale);
            var image = WithMargin(TextOnDark(area));
            var readings = new List<string>();
            foreach (var reader in TextReaders.All) {
                foreach (var reading in await reader.ReadAsync(image)) {
                    var text = Clean(reading);
                    if (text.Length > 0 && !readings.Contains(text)) {
                        readings.Add(text);
                    }
                }
            }
            return readings;
        }

        // --- A Grandmaster's ladder position ---

        // The position is printed big and white across the bottom of the portrait ("45", "#6"), over the blue
        // lightning frame. In card units:
        private const int NumberTop = 78, NumberHeight = 70;
        private const int NumberLeftX = 16, NumberRightX = 270, NumberWidth = 128;

        // A character: near-white (every channel above this), 22-56 units tall, not touching the area's edge
        // (the lightning does).
        private const float CharacterWhite = 140;
        private const int CharacterMinHeight = 22, CharacterMaxHeight = 56;
        // How far (units) a character's top may sit off the number's line: the samples' characters are within 3.
        private const int CharacterLineTolerance = 6;

        public const int GlyphWidth = 16, GlyphHeight = 24;

        /// <summary>How well a character must match its template to be taken from it.</summary>
        public const double GlyphCutoff = 0.7;

        /// <summary>
        /// A Grandmaster card's ladder position, or null when it can't be read. Each character is matched
        /// against <paramref name="templates"/>' Grandmaster characters; if one matches none of them (a digit
        /// no sample has shown yet), the text reader reads the characters instead - Windows' reads numbers of two digits
        /// or more well once they're picked out clean, but not single digits.
        /// </summary>
        public static async Task<int?> ReadGrandmasterNumberAsync(Pixels strip, Side side, int slot, RankTemplates templates)
        {
            var characters = GrandmasterCharacters(strip, side, slot);
            if (characters.Count == 0) {
                return null;
            }

            var read = new StringBuilder();
            var allKnown = templates?.GrandmasterCharacters != null;
            foreach (var c in characters) {
                var (character, score) = allKnown ? MatchCharacter(c.Glyph, templates) : ('?', 0);
                if (score < GlyphCutoff) {
                    allKnown = false;
                    break;
                }
                read.Append(character);
            }
            if (allKnown) {
                return ParseNumber(read.ToString());
            }

            // Windows does better with more to go on: the characters three times over, with a gap between.
            var clean = CharactersImage(characters);
            var reader = TextReaders.Digits;
            if (reader == null) {
                return null;
            }
            foreach (var reading in await reader.ReadAsync(WithMargin(Repeat(clean, 3)))) {
                if (ParseNumber(reading) is int number) {
                    return number;
                }
            }
            return null;
        }

        /// <summary>The first run of digits in what was read ("#6" is 6), if it's a plausible ladder position.</summary>
        public static int? ParseNumber(string text)
        {
            var digits = new string((text ?? "").SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) && n >= 1 && n <= 999 ? n : null;
        }

        /// <summary>One character picked out of the number, left to right: its shape, and where it was.</summary>
        public sealed record Character(int Left, int Top, int Width, int Height, bool[] Mask, float[] Glyph);

        /// <summary>The number's characters, left to right, each as a clean shape.</summary>
        public static List<Character> GrandmasterCharacters(Pixels strip, Side side, int slot)
        {
            var area = LoadingScreenReader.SampleCardArea(strip, side, slot, side == Side.Left ? NumberLeftX : NumberRightX, NumberTop, NumberWidth, NumberHeight);
            int w = area.Width, h = area.Height;
            var white = new bool[w * h];
            for (var i = 0; i < w * h; i++) {
                white[i] = Math.Min(area.Rgb[i * 3], Math.Min(area.Rgb[i * 3 + 1], area.Rgb[i * 3 + 2])) > CharacterWhite;
            }

            var labels = new int[w * h];
            var characters = new List<Character>();
            var next = 0;
            for (var start = 0; start < w * h; start++) {
                if (!white[start] || labels[start] != 0) {
                    continue;
                }
                next++;
                int top = h, bottom = 0, left = w, right = 0;
                var pixels = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start);
                labels[start] = next;
                while (queue.Count > 0) {
                    var p = queue.Dequeue();
                    pixels.Add(p);
                    int x = p % w, y = p / w;
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }) {
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h || !white[ny * w + nx] || labels[ny * w + nx] != 0) {
                            continue;
                        }
                        labels[ny * w + nx] = next;
                        queue.Enqueue(ny * w + nx);
                    }
                }
                var height = bottom - top + 1;
                var width = right - left + 1;
                var touchesEdge = top == 0 || left == 0 || bottom == h - 1 || right == w - 1;
                if (height < CharacterMinHeight || height > CharacterMaxHeight || width > height * 1.3 || touchesEdge) {
                    continue;
                }
                var mask = new bool[width * height];
                foreach (var p in pixels) {
                    mask[(p / w - top) * width + p % w - left] = true;
                }
                characters.Add(new Character(left, top, width, height, mask, Glyph(mask, width, height)));
            }
            // A number's characters stand on one line. Something white in the portrait above it (#62, #32 in
            // the samples: a blob at the top, ~15 units higher) is dropped by being off that line.
            if (characters.Count > 1) {
                var tops = characters.Select(c => c.Top).OrderBy(t => t).ToList();
                var lineTop = tops[tops.Count / 2];
                characters = characters.Where(c => Math.Abs(c.Top - lineTop) <= CharacterLineTolerance).ToList();
            }
            return characters.OrderBy(c => c.Left).ToList();
        }

        /// <summary>A character's shape scaled to <see cref="GlyphWidth"/> x <see cref="GlyphHeight"/> and normalised.</summary>
        private static float[] Glyph(bool[] mask, int width, int height)
        {
            var image = new Pixels(width, height);
            for (var i = 0; i < mask.Length; i++) {
                image.Rgb[i * 3] = image.Rgb[i * 3 + 1] = image.Rgb[i * 3 + 2] = mask[i] ? 255 : 0;
            }
            var small = image.Sample(0, 0, width / (double)GlyphWidth, height / (double)GlyphHeight, GlyphWidth, GlyphHeight);
            return LoadingScreenReader.Normalise(small.GrayValues());
        }

        /// <summary>The template character a glyph matches best, and how well (1 is identical).</summary>
        public static (char Character, double Score) MatchCharacter(float[] glyph, RankTemplates templates)
        {
            var best = ('?', double.MinValue);
            for (var i = 0; i < templates.GrandmasterCharacters.Length; i++) {
                var score = LoadingScreenReader.Dot(glyph, templates.GrandmasterGlyphs[i]);
                if (score > best.Item2) {
                    best = (templates.GrandmasterCharacters[i], score);
                }
            }
            return best;
        }

        /// <summary>The characters drawn black on white, in place, with nothing else.</summary>
        private static Pixels CharactersImage(List<Character> characters)
        {
            var left = characters.Min(c => c.Left);
            var top = characters.Min(c => c.Top);
            var width = characters.Max(c => c.Left + c.Width) - left;
            var height = characters.Max(c => c.Top + c.Height) - top;
            var image = new Pixels(width, height);
            Array.Fill(image.Rgb, 255f);
            foreach (var c in characters) {
                for (var y = 0; y < c.Height; y++) {
                    for (var x = 0; x < c.Width; x++) {
                        if (c.Mask[y * c.Width + x]) {
                            var o = ((c.Top - top + y) * width + c.Left - left + x) * 3;
                            image.Rgb[o] = image.Rgb[o + 1] = image.Rgb[o + 2] = 0;
                        }
                    }
                }
            }
            return image;
        }

        /// <summary><paramref name="image"/> <paramref name="times"/> times side by side, a character's width apart.</summary>
        private static Pixels Repeat(Pixels image, int times)
        {
            var gap = image.Height / 2;
            var result = new Pixels(image.Width * times + gap * (times - 1), image.Height);
            Array.Fill(result.Rgb, 255f);
            for (var k = 0; k < times; k++) {
                for (var y = 0; y < image.Height; y++) {
                    Array.Copy(image.Rgb, y * image.Width * 3, result.Rgb, (y * result.Width + k * (image.Width + gap)) * 3, image.Width * 3);
                }
            }
            return result;
        }

        // --- Shared ---

        /// <summary>
        /// Light text on the card's dark, busy background, made dark-on-white for the reader: each pixel's
        /// brightest channel (so red, blue and green names all come out strong), inverted.
        /// </summary>
        public static Pixels TextOnDark(Pixels area)
        {
            var result = new Pixels(area.Width, area.Height);
            for (var i = 0; i < area.Width * area.Height; i++) {
                var v = 255 - Math.Max(area.Rgb[i * 3], Math.Max(area.Rgb[i * 3 + 1], area.Rgb[i * 3 + 2]));
                result.Rgb[i * 3] = result.Rgb[i * 3 + 1] = result.Rgb[i * 3 + 2] = v;
            }
            return result;
        }

        /// <summary>The image on a white margin <see cref="Margin"/> pixels wide.</summary>
        public static Pixels WithMargin(Pixels image)
        {
            var result = new Pixels(image.Width + Margin * 2, image.Height + Margin * 2);
            Array.Fill(result.Rgb, 255f);
            for (var y = 0; y < image.Height; y++) {
                Array.Copy(image.Rgb, y * image.Width * 3, result.Rgb, ((y + Margin) * result.Width + Margin) * 3, image.Width * 3);
            }
            return result;
        }

        /// <summary>What was read, without the spaces the reader likes to put in names and stray marks around them.</summary>
        private static string Clean(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text ?? "") {
                if (char.IsLetterOrDigit(c)) {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
