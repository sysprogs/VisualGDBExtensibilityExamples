using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// Interaction logic for ImageWatchConfigurator.xaml
    /// </summary>
    public partial class ImageWatchConfigurator : UserControl, IVisualExpressionConfigurator
    {
        public ImageWatchConfigurator(IVisualExpressionParsingContext ctx)
        {
            Controller = new ControllerImpl(ctx);
            InitializeComponent();
            Controller.PropertyChanged += (s, e) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        }


        public class ControllerImpl : INotifyPropertyChanged
        {
            private readonly ImageWatchPreferences _Preferences;
            public ImageWatchSettings Settings;
            private IVisualExpressionParsingContext _Context;

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

           
            public ControllerImpl(IVisualExpressionParsingContext ctx)
            {
                _Preferences = ctx.Preferences as ImageWatchPreferences;
                Settings = (ctx.PersistentSettings as ImageWatchSettings)?.Clone();
                if (Settings?.WidthExpression == null && Settings?.HeightExpression == null)
                    Settings = _Preferences?.LookupSettings(ctx)?.Clone() ?? new ImageWatchSettings();

                _Context = ctx;
                PixelFormat = PixelFormats.FirstOrDefault(f => f.ID == _Preferences?.DefaultPixelFormat);
            }

            public string WidthExpression
            {
                get => Settings.WidthExpression;
                set
                {
                    Settings.WidthExpression = value;
                    OnPropertyChanged(nameof(WidthExpression));
                }
            }

            public string HeightExpression
            {
                get => Settings.HeightExpression;
                set
                {
                    Settings.HeightExpression = value;
                    OnPropertyChanged(nameof(HeightExpression));
                }
            }

            public string FileSizeExpression
            {
                get => Settings.FileSizeExpression;
                set
                {
                    Settings.FileSizeExpression = value;
                    OnPropertyChanged(nameof(FileSizeExpression));
                }
            }

            public string FramebufferExpression
            {
                get => Settings.FrameBufferExpression;
                set
                {
                    Settings.FrameBufferExpression = value;
                    OnPropertyChanged(nameof(FramebufferExpression));
                }
            }

            public IPixelFormatAdapter[] PixelFormats => PixelFormatAdapters.AllFormats;

            public IPixelFormatAdapter PixelFormat
            {
                get => PixelFormats.FirstOrDefault(a => a.ID == Settings.PixelFormat);
                set
                {
                    Settings.PixelFormat = value?.ID;
                    if (_Preferences != null)
                        _Preferences.DefaultPixelFormat = value?.ID;
                    OnPropertyChanged(nameof(PixelFormat));
                    OnPropertyChanged(nameof(DimensionsVisibility));
                    OnPropertyChanged(nameof(SizeVisibility));
                }
            }

            public Visibility DimensionsVisibility => (Settings.PixelFormat == PixelFormatAdapters.EncodedFormatID) ? Visibility.Collapsed : Visibility.Visible;
            public Visibility SizeVisibility => (Settings.PixelFormat == PixelFormatAdapters.EncodedFormatID) ? Visibility.Visible : Visibility.Collapsed;

            internal string ValidateSettings()
            {
                if (PixelFormat is PixelFormatAdapters.EncodedFormat)
                {
                    if (((_Context.EvaluateSubexpression(FileSizeExpression ?? throw new Exception("Please specify the file size expression")) as ExpressionValue.Integral)?.Value ?? 0) == 0)
                        return "Could not evaluate width";
                }
                else
                {
                    if (((_Context.EvaluateSubexpression(WidthExpression ?? throw new Exception("Please specify the width expression")) as ExpressionValue.Integral)?.Value ?? 0) == 0)
                        return "Could not evaluate width";
                    if (((_Context.EvaluateSubexpression(HeightExpression ?? throw new Exception("Please specify the height expression")) as ExpressionValue.Integral)?.Value ?? 0) == 0)
                        return "Could not evaluate height";

                    if (PixelFormat == null)
                        return "Please select a pixel format";
                }

                return null;
            }
        }

        public ControllerImpl Controller { get; }

        public UIElement Control => this;

        public event EventHandler SettingsChanged;

        public string ValidateSettings() => Controller.ValidateSettings();

        public VisualExpressionSettingsBase GetFinalSettings()
        {
            return Controller.Settings;
        }

        public void Dispose()
        {
        }
    }
}
