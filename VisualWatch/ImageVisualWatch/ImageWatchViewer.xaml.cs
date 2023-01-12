using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public partial class ImageWatchViewer : UserControl, IVisualExpressionViewer, INotifyPropertyChanged
    {
        private ParsedImage _Image;
        private BitmapSource _Bitmap;

        public ImageWatchViewer(ImageWatchPreferences preferences)
        {
            InitializeComponent();
            _Preferences = preferences;
            if (preferences != null)
                ViewMode = preferences.ViewMode;
        }

        public UIElement Control => this;
        public UIElement[] ToolbarControls => (Resources["ToolbarButtons"] as ArrayExtension)?.Items.OfType<UIElement>().ToArray();

        public bool SupportsMultipleExpressions => false;

        public event EventHandler<VisualExpressionErrorEventArgs> Error;
        public event EventHandler PreferencesChanged;
        public event PropertyChangedEventHandler PropertyChanged;

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

            var screenPos = (Vector)e.GetPosition(this) - new Vector(Math.Round(ActualWidth / 2), Math.Round(ActualHeight / 2));

            _ScreenOffset = screenPos - (screenPos - _ScreenOffset) * (_ZoomLevel / oldZoom);
            ApplyReasonableViewportLimits();

            if (_ZoomLevel != oldZoom)
                ViewMode = ImageWatchViewMode.Custom;

            InvalidateVisual();
        }

        private void ApplyReasonableViewportLimits()
        {
            if (_Bitmap == null)
                return;

            //1. Prevent from zooming out beyond 1:1, unless it's needed to fit the entire image
            double zoomLevelToFitImage = Math.Min(ActualWidth / _Bitmap.Width, ActualHeight / _Bitmap.Height);
            double minimumReasonableZoomLevel = Math.Min(1, zoomLevelToFitImage);
            
            _ZoomLevel = Math.Max(_ZoomLevel, minimumReasonableZoomLevel);

            //2. Prevent from panning the image too much
            Vector scaledImageSize = new Vector(_Bitmap.Width, _Bitmap.Height) * _ZoomLevel;
            Vector halfExcessiveImageSize = (scaledImageSize - new Vector(ActualWidth, ActualHeight)) / 2;
            double maxHorizontalPan = Math.Max(0, halfExcessiveImageSize.X), maxVerticalPan = Math.Max(0, halfExcessiveImageSize.Y);

            if (Math.Abs(_ScreenOffset.X) > maxHorizontalPan)
                _ScreenOffset.X = Math.Sign(_ScreenOffset.X) * maxHorizontalPan;
            if (Math.Abs(_ScreenOffset.Y) > maxVerticalPan)
                _ScreenOffset.Y = Math.Sign(_ScreenOffset.Y) * maxVerticalPan;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _ScreenOffset = _MoveBase + (Vector)e.GetPosition(this);
                ApplyReasonableViewportLimits();
                InvalidateVisual();
                ViewMode = ImageWatchViewMode.Custom;
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

        public ImageWatchViewMode[] ViewModes { get; } = new[] { ImageWatchViewMode.ZoomedOut, ImageWatchViewMode.OneToOne, ImageWatchViewMode.ScaleToFit, ImageWatchViewMode.Custom };


        ImageWatchViewMode _ViewMode = ImageWatchViewMode.Custom;
        private readonly ImageWatchPreferences _Preferences;

        public ImageWatchViewMode ViewMode
        {
            get => _ViewMode;
            set
            {
                if (_ViewMode == value)
                    return;
                _ViewMode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewMode)));

                if (value != ImageWatchViewMode.Custom && _Bitmap != null)
                {
                    switch (value)
                    {
                        case ImageWatchViewMode.ZoomedOut:
                            _ZoomLevel = 0; //The call below will correct it to the minimum reasonable level
                            break;
                        case ImageWatchViewMode.OneToOne:
                            _ZoomLevel = 1;
                            break;
                        case ImageWatchViewMode.ScaleToFit:
                            _ZoomLevel = Math.Min(ActualWidth / _Bitmap.Width, ActualHeight / _Bitmap.Height);
                            break;
                    }

                    _ScreenOffset = default;
                    if (_Preferences != null)
                        _Preferences.ViewMode = value;
                    PreferencesChanged?.Invoke(this, EventArgs.Empty);
                    ApplyReasonableViewportLimits();
                    InvalidateVisual();
                }
            }
        }
    }

    public enum ImageWatchViewMode
    {
        ZoomedOut,
        OneToOne,
        ScaleToFit,
        Custom,
    }
}
