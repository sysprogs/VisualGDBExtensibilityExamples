using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
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
        private BitmapSource _Bitmap;

        public ImageWatchViewer()
        {
            InitializeComponent();
        }

        public UIElement Control => this;

        public bool SupportsMultipleExpressions => false;

        public event EventHandler<VisualExpressionErrorEventArgs> Error;

        public IVisualizedExpression AddExpression(IParsedVisualExpression expr, bool isOutdated)
        {
            _Bitmap = (expr as ParsedImage)?.ToBitmapSource();
            InvalidateVisual();
            return null;
        }

        public void Dispose()
        {
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            try
            {
                if (_Bitmap != null)
                {
                    double x = (ActualWidth - _Bitmap.Width) / 2, y = (ActualHeight - _Bitmap.Height) / 2;
                    drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
                    drawingContext.DrawImage(_Bitmap, new Rect(x, y, _Bitmap.Width, _Bitmap.Height));
                    drawingContext.Pop();
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new VisualExpressionErrorEventArgs(ex, "failed to render image"));
            }
        }
    }

}
