using System.Windows;
using System.Windows.Media;

namespace ProjectManagementBoard.Controls
{
    internal static class VisualTreeExtensions
    {
        public static T? FindDescendant<T>(this DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                if (child.FindDescendant<T>() is { } deeper) return deeper;
            }
            return null;
        }

        /// <summary>Walks up from <paramref name="d"/> (inclusive) to <paramref name="stopAt"/> (exclusive).</summary>
        public static IEnumerable<DependencyObject> SelfAndAncestors(this DependencyObject? d, DependencyObject? stopAt = null)
        {
            while (d != null && d != stopAt)
            {
                yield return d;
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
        }
    }
}
