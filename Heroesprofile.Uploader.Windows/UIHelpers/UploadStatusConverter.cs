using Heroesprofile.Uploader.Common;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace Heroesprofile.Uploader.Windows.UIHelpers
{
    public class UploadStatusConverter : GenericValueConverter<UploadStatus, string>
    {
        protected override string Convert(UploadStatus value)
        {
            if (value == UploadStatus.None) {
                return "";
            }
            // Convert "EnumItems" to "Enum items"
            return Regex.Replace(value.ToString(), "([a-z])([A-Z])", m => $"{m.Groups[1].Value} {m.Groups[2].Value.ToLower()}");
        }
    }
}
