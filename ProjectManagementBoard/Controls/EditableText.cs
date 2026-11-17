using System.Windows;
using System.Windows.Controls;

namespace ProjectManagementBoard.Controls
{
    /// <summary>
    /// Read-only text that opens the edit popup when clicked (see <see cref="ToolCardView"/> /
    /// <see cref="GroupView"/>). <see cref="Field"/> tells the host which editor to open. Template lives in App.xaml.
    /// </summary>
    public class EditableText : Control
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(EditableText), new PropertyMetadata(""));

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(EditableText), new PropertyMetadata(""));

        public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
            nameof(TextAlignment), typeof(TextAlignment), typeof(EditableText), new PropertyMetadata(TextAlignment.Left));

        public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
            nameof(Field), typeof(EditableField), typeof(EditableText), new PropertyMetadata(EditableField.Title));

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
        public TextAlignment TextAlignment { get => (TextAlignment)GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
        public EditableField Field { get => (EditableField)GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    }

    public enum EditableField { Title, Description, GroupTitle }
}
