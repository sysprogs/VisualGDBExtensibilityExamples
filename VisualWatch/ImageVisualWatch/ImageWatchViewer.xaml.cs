using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VisualGDBExpressions;

namespace ImageVisualWatch
{
    /// <summary>
    /// Interaction logic for ImageWatchViewer.xaml
    /// </summary>
    public partial class ImageWatchViewer : UserControl, IVisualExpressionViewer
    {
        private ParsedImage _Image;
        private BitmapSource _Bitmap;

        public ImageWatchViewer()
        {
            InitializeComponent();
        }

        public UIElement Control => this;
        public UIElement[] ToolbarControls => (Resources["ToolbarButtons"] as ArrayExtension)?.Items.OfType<UIElement>().ToArray();

        public bool SupportsMultipleExpressions => false;

        public event EventHandler<VisualExpressionErrorEventArgs> Error;

        public IVisualizedExpression AddExpression(IParsedVisualExpression expr, bool isOutdated)
        {
            _Image = expr as ParsedImage;
            _Bitmap = null;
            _ZoomLevel = 1;
            _ScreenOffset = default;
            InvalidateVisual();
            return null;
        }

        public void Dispose()
        {
        }

        double _ZoomLevel = 1;
        Vector _ScreenOffset;
        Vector _MoveBase;

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            try
            {
                if (_Image != null && _Bitmap == null)
                    _Bitmap = _Image?.ToBitmapSource(PresentationSource.FromVisual(this));

                if (_Bitmap != null)
                {
                    double centerX = ActualWidth / 2, centerY = ActualHeight / 2;
                    double renderWidth = _Bitmap.Width * _ZoomLevel, renderHeight = _Bitmap.Height * _ZoomLevel;

                    drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
                    var screenRect = new Rect(Math.Round(centerX - renderWidth / 2), Math.Round(centerY - renderHeight / 2), renderWidth, renderHeight);
                    screenRect.Offset(_ScreenOffset);
                    drawingContext.DrawImage(_Bitmap, screenRect);
                    drawingContext.Pop();
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new VisualExpressionErrorEventArgs(ex, "failed to render image"));
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            var oldZoom = _ZoomLevel;
            double scaling = Math.Pow(0.9, (double)e.Delta / 100);
            _ZoomLevel /= scaling;
            _ZoomLevel = Math.Max(_ZoomLevel, 1);

            var screenPos = (Vector)e.GetPosition(this) - new Vector(Math.Round(ActualWidth / 2), Math.Round(ActualHeight / 2));

            _ScreenOffset = screenPos - (screenPos - _ScreenOffset) * (_ZoomLevel / oldZoom);
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _ScreenOffset = _MoveBase + (Vector)e.GetPosition(this);
                InvalidateVisual();
            }
        }


        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                _MoveBase = _ScreenOffset - (Vector)e.GetPosition(this);

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _ZoomLevel = 1;
            _ScreenOffset = default;
            InvalidateVisual();
        }
    }

}
