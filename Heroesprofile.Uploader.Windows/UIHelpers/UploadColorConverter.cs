using Heroesprofile.Uploader.Common;
using System;
using System.Linq;
using System.Windows.Media;

namespace Heroesprofile.Uploader.Windows.UIHelpers
{
    public class UploadColorConverter : GenericValueConverter<UploadStatus, Brush>
    {
        protected override Brush Convert(UploadStatus value)
        {
            switch (value) {
                case UploadStatus.Success:
                    return GetBrush("StatusUploadSuccessBrush");

                case UploadStatus.InProgress:
                    return GetBrush("StatusUploadInProgressBrush");

                case UploadStatus.Duplicate:
                case UploadStatus.AiDetected:
                case UploadStatus.CustomGame:
                case UploadStatus.PtrRegion:
                case UploadStatus.TooOld:
                case UploadStatus.NotSupported:
                    return GetBrush("StatusUploadNeutralBrush");

                case UploadStatus.None:
                case UploadStatus.UploadError:
                case UploadStatus.Incomplete:
                default:
                    return GetBrush("StatusUploadFailedBrush");
            }
        }

        private Brush GetBrush(string key)
        {
            return App.Current.Resources[key] as Brush;
        }
    }
}
