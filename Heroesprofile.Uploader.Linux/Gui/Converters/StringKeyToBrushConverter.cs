using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Heroesprofile.Uploader.Linux.Gui.Converters
{
    /// <summary>A resource key string (e.g. MainWindowViewModel.OverallStatusBrushKey) -> its current-theme brush.</summary>
    public class StringKeyToBrushConverter : IValueConverter
    {
        public static readonly StringKeyToBrushConverter Instance = new StringKeyToBrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ResourceBrushes.Resolve(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
