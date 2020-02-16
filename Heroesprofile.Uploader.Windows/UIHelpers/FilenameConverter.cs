using Heroesprofile.Uploader.Common;
using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Heroesprofile.Uploader.Windows.UIHelpers
{
    public class FilenameConverter : GenericValueConverter<string, string>
    {
        protected override string Convert(string value)
        {
            return Path.GetFileName(value);
        }
    }
}
