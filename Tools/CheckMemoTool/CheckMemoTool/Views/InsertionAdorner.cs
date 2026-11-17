using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CheckMemoTool.Views
{
    // 드래그 중 놓일 자리를 보여주는 가이드 선 (F-12)
    public class InsertionAdorner : Adorner
    {
        private static readonly Pen LinePen = CreatePen();
        private double _y;

        public InsertionAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public double Y
        {
            get { return _y; }
            set
            {
                if (_y == value) return;
                _y = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = AdornedElement.RenderSize.Width;
            double y = _y;
            dc.DrawLine(LinePen, new Point(6, y), new Point(width - 6, y));
            dc.DrawEllipse(LinePen.Brush, null, new Point(6, y), 3, 3);
        }

        private static Pen CreatePen()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6F, 0xA5));
            brush.Freeze();
            var pen = new Pen(brush, 2);
            pen.Freeze();
            return pen;
        }
    }
}
