using Avalonia.Data.Converters;
using Avalonia.Media;
using Heroesprofile.Uploader.Common;
using System;
using System.Globalization;

namespace Heroesprofile.Uploader.Linux.Gui.Converters
{
    /// <summary>
    /// UploadStatus -> the current theme's status brush (foreground or ~22%-alpha badge background),
    /// resolved live against Application.Current.ActualThemeVariant. Since the lookup only re-runs
    /// when the bound UploadStatus value's PropertyChanged fires, view models re-raise that on a
    /// theme switch so already-realized rows/chips pick up the new palette (see
    /// ReplayRowViewModel.TouchForThemeRefresh / StatChipViewModel.TouchForThemeRefresh).
    /// </summary>
    public class UploadStatusToBrushConverter : IValueConverter
    {
        public static readonly UploadStatusToBrushConverter Foreground = new UploadStatusToBrushConverter(background: false);
        public static readonly UploadStatusToBrushConverter Background = new UploadStatusToBrushConverter(background: true);

        private readonly bool _background;

        private UploadStatusToBrushConverter(bool background)
        {
            _background = background;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not UploadStatus status) {
                return Brushes.Transparent;
            }

            var key = _background ? StatusPresentation.BgBrushKey(status) : StatusPresentation.BrushKey(status);
            return ResourceBrushes.Resolve(key);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
