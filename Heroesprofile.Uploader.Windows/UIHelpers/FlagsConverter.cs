using System;
using System.Linq;
using System.Windows;

namespace Heroesprofile.Uploader.Windows.UIHelpers
{
    public class FlagsConverter : GenericValueConverter<Enum, bool, Enum>
    {
        protected override bool Convert(Enum value, Enum parameter)
        {
            return value.HasFlag(parameter);
        }

        protected override Enum ConvertBack(bool value, Enum parameter)
        {
            // I was unable to find how to get source binding value, so let's use a dirty hack
            var val = App.Settings.DeleteAfterUpload;

            if (value) {
                val |= (Common.DeleteFiles)parameter;
            } else {
                val &= ~(Common.DeleteFiles)parameter;
            }
            return val;
        }
    }
}
