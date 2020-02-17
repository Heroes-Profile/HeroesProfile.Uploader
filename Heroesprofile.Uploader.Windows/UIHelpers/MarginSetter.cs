using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Heroesprofile.Uploader.Windows.UIHelpers
{
    internal class MarginSetter
    {
        public static Thickness GetMargin(DependencyObject obj)
        {
            return (Thickness)obj.GetValue(MarginProperty);
        }

        public static void SetMargin(DependencyObject obj, Thickness value)
        {
            obj.SetValue(MarginProperty, value);
        }

        public static readonly DependencyProperty MarginProperty =
            DependencyProperty.RegisterAttached("Margin", typeof(Thickness), typeof(MarginSetter), new UIPropertyMetadata(new Thickness(), CreateThicknesForChildren));

        public static void CreateThicknesForChildren(object sender, DependencyPropertyChangedEventArgs e)
        {
            var panel = sender as Panel;
            if (panel == null) return;
            panel.Loaded += (q, w) => CreateThicknesForChildrenInner(panel);
            CreateThicknesForChildrenInner(panel);
        }

        private static void CreateThicknesForChildrenInner(Panel panel)
        {
            var zero = new Thickness(0);
            foreach (var child in panel.Children) {
                var fe = child as FrameworkElement;
                if (fe == null) continue;
                if (fe.Margin == zero)
                    fe.Margin = MarginSetter.GetMargin(panel);
            }
        }
    }
}
