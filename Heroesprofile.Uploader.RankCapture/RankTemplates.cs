using System;
using System.IO;

namespace Heroesprofile.Uploader.RankCapture
{
    /// <summary>
    /// What <see cref="LoadingScreenReader"/> compares cards against, averaged from labelled loading-screen
    /// samples: the Master and Grandmaster portrait frames, the division badge's shield outline, the
    /// digits 1-5, and the characters of Grandmaster ladder positions. Shipped inside the assembly
    /// (Templates/rank-templates.bin); rebuilt from the samples by the RankCapture tests' explicit
    /// Build_templates_from_the_samples test.
    /// </summary>
    internal sealed class RankTemplates
    {
        private const string ResourceName = "Heroesprofile.Uploader.RankCapture.rank-templates.bin";
        private const int FormatVersion = 2;

        /// <summary>The Master frame: <see cref="LoadingScreenReader.FrameRing"/> of the portrait, normalised.</summary>
        public float[] Master { get; init; }

        /// <summary>The Grandmaster frame (lightning around it), the same way.</summary>
        public float[] Grandmaster { get; init; }

        /// <summary>The badge area's edge strength (<see cref="LoadingScreenReader.ShieldEdges"/>).</summary>
        public float[] Shield { get; init; }

        /// <summary>The digits 1-5 (index 0 is 1), each <see cref="LoadingScreenReader.DigitGlyph"/>-shaped.</summary>
        public float[][] Digits { get; init; }

        /// <summary>
        /// The characters seen in Grandmaster ladder positions so far ('0'-'9', '#'), and each one's shape
        /// (<see cref="CardText.GrandmasterCharacters"/>), in the same order.
        /// </summary>
        public char[] GrandmasterCharacters { get; init; }
        public float[][] GrandmasterGlyphs { get; init; }

        private static readonly Lazy<RankTemplates> _shipped = new Lazy<RankTemplates>(LoadShipped);

        /// <summary>The templates shipped in this assembly, or null if it was built without them.</summary>
        public static RankTemplates Shipped => _shipped.Value;

        private static RankTemplates LoadShipped()
        {
            using var stream = typeof(RankTemplates).Assembly.GetManifestResourceStream(ResourceName);
            return stream == null ? null : Read(stream);
        }

        public static RankTemplates Read(Stream stream)
        {
            using var reader = new BinaryReader(stream);
            var version = reader.ReadInt32();
            if (version != FormatVersion) {
                throw new InvalidDataException($"Rank templates are format {version}, this reader knows {FormatVersion}");
            }
            float[] Array()
            {
                var values = new float[reader.ReadInt32()];
                for (var i = 0; i < values.Length; i++) {
                    values[i] = reader.ReadSingle();
                }
                return values;
            }
            var master = Array();
            var grandmaster = Array();
            var shield = Array();
            var digits = new float[reader.ReadInt32()][];
            for (var i = 0; i < digits.Length; i++) {
                digits[i] = Array();
            }
            var characters = new char[reader.ReadInt32()];
            var glyphs = new float[characters.Length][];
            for (var i = 0; i < characters.Length; i++) {
                characters[i] = reader.ReadChar();
                glyphs[i] = Array();
            }
            return new RankTemplates {
                Master = master,
                Grandmaster = grandmaster,
                Shield = shield,
                Digits = digits,
                GrandmasterCharacters = characters,
                GrandmasterGlyphs = glyphs,
            };
        }

        public void Write(Stream stream)
        {
            using var writer = new BinaryWriter(stream);
            writer.Write(FormatVersion);
            void Array(float[] values)
            {
                writer.Write(values.Length);
                foreach (var v in values) {
                    writer.Write(v);
                }
            }
            Array(Master);
            Array(Grandmaster);
            Array(Shield);
            writer.Write(Digits.Length);
            foreach (var digit in Digits) {
                Array(digit);
            }
            writer.Write(GrandmasterCharacters.Length);
            for (var i = 0; i < GrandmasterCharacters.Length; i++) {
                writer.Write(GrandmasterCharacters[i]);
                Array(GrandmasterGlyphs[i]);
            }
        }
    }
}
