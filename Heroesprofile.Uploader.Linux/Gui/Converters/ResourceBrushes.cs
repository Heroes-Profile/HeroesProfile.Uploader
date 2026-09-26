using Avalonia;
using Avalonia.Media;

namespace Heroesprofile.Uploader.Linux.Gui.Converters
{
    /// <summary>Shared "look up a brush resource key against the current theme" used by the converters below.</summary>
    internal static class ResourceBrushes
    {
        public static IBrush Resolve(string key)
        {
            if (key != null && Application.Current != null &&
                Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource) &&
                resource is IBrush brush) {
                return brush;
            }
            return Brushes.Transparent;
        }
    }
}
