using Avalonia;
using Heroesprofile.Uploader.Common;

namespace Heroesprofile.Uploader.Desktop.Gui
{
    /// <summary>
    /// <c>gui:StatusClass.Status="{Binding UploadStatus}"</c> gives a control the style class of that
    /// status - "stSuccess", "stUploadError", ... (and none for <see cref="UploadStatus.None"/>) - so
    /// styles pick each status's colour (Theme 2's badges and stat dots, see Theme/Theme2.axaml). Colours
    /// come from DynamicResource setters there, so they follow light/dark switches by themselves.
    /// </summary>
    public static class StatusClass
    {
        public static readonly AttachedProperty<UploadStatus> StatusProperty =
            AvaloniaProperty.RegisterAttached<StyledElement, UploadStatus>("Status", typeof(StatusClass));

        static StatusClass()
        {
            StatusProperty.Changed.AddClassHandler<StyledElement>((element, e) => {
                element.Classes.Remove(ClassFor(e.GetOldValue<UploadStatus>()));
                if (e.GetNewValue<UploadStatus>() is var status && status != UploadStatus.None) {
                    element.Classes.Add(ClassFor(status));
                }
            });
        }

        public static string ClassFor(UploadStatus status) => "st" + status;

        public static UploadStatus GetStatus(StyledElement element) => element.GetValue(StatusProperty);

        public static void SetStatus(StyledElement element, UploadStatus value) => element.SetValue(StatusProperty, value);
    }
}
