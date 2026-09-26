using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Heroesprofile.Uploader.Linux.Gui.Converters
{
    /// <summary>Small ad-hoc converters for the settings dialog's and empty state's inline status lines.</summary>
    public static class BoolConverters
    {
        /// <summary>true -> success brush, false -> error brush - the ok/bad colour for status lines.</summary>
        public static readonly IValueConverter StatusBrush =
            new FuncValueConverter<bool, IBrush>(ok => ResourceBrushes.Resolve(ok ? "StatusSuccessBrush" : "StatusErrorBrush"));

        /// <summary>Hides an inline status line until there's something to say.</summary>
        public static readonly IValueConverter NotEmpty =
            new FuncValueConverter<string, bool>(s => !string.IsNullOrEmpty(s));
    }
}
