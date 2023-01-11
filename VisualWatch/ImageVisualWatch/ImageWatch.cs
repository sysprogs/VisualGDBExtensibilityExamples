using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VisualGDBExpressions;

namespace ImageVisualWatch
{
    public class ImageWatchProvider : IVisualExpressionProvider
    {
        public string ID => "com.sysprogs.imagewatch";

        public string UserFriendlyName => "Image";

        public Type SettingsType => typeof(ImageWatchSettings);
        public Type PreferencesType => null;

        public IVisualExpressionConfigurator CreateConfigurator(IVisualExpressionParsingContext ctx) => new ImageWatchConfigurator(ctx);

        public IVisualExpressionViewer CreateViewer() => new ImageWatchViewer();

        public IParsedVisualExpression ParseExpression(IVisualExpressionParsingContext ctx)
        {
            var settings = ctx.PersistentSettings as ImageWatchSettings ?? new ImageWatchSettings();
            if (string.IsNullOrEmpty(settings.WidthExpression) || string.IsNullOrEmpty(settings.HeightExpression))
                settings = ctx.ShowModalSettingsDialog(new ImageWatchConfigurator(ctx)) as ImageWatchSettings ?? throw new OperationCanceledException();

            var width = (int)(ctx.EvaluateSubexpression(settings.WidthExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the width")).Value;
            var height = (int)(ctx.EvaluateSubexpression(settings.WidthExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the height")).Value;

            ulong framebufferAddress;
            if (string.IsNullOrEmpty(settings.FrameBufferExpression))
                framebufferAddress = ctx.Address ?? throw new Exception($"Could not compute address of '{ctx.RawExpression}'");
            else
                framebufferAddress = (ctx.EvaluateSubexpression(settings.FrameBufferExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the height")).Value;

            var pixelFormat = PixelFormatAdapters.AllFormats.FirstOrDefault(f => f.ID == settings.PixelFormat) ?? throw new Exception("Unknown pixel format: " + settings.PixelFormat);
            var data = ctx.Evaluator.ReadMemory(framebufferAddress, pixelFormat.GetBufferSizeInBytes(width, height)) ?? throw new Exception("Failed to read framebuffer contents");

            settings.LastKnownWidth = width;
            settings.LastKnownHeight = height;

            return new ParsedImage(pixelFormat.Translate(data, width, height), settings, ctx.RawExpression);
        }

        public int Probe(IVisualExpressionParsingContext ctx)
        {
            return VisualExpressionConstants.DefaultProbeScore - 10;
        }
    }

    class ParsedImage : IParsedVisualExpression
    {
        private TranslatedImage _Image;
        private ImageWatchSettings _Settings;
        public readonly string Name;

        public ParsedImage(TranslatedImage image, ImageWatchSettings settings, string rawExpression)
        {
            _Image = image;
            _Settings = settings;
            Name = rawExpression;
        }

        public BitmapSource ToBitmapSource(PresentationSource ps) => ToBitmapSource(ps.CompositionTarget.TransformToDevice.M11, ps.CompositionTarget.TransformToDevice.M22);
        public BitmapSource ToBitmapSource(double xScale, double yScale) => BitmapSource.Create(_Settings.LastKnownWidth, _Settings.LastKnownHeight, 96 * xScale, 96 * yScale, _Image.Format, null, _Image.Data, _Image.Data.Length / _Settings.LastKnownHeight);

        public bool CanExport => true;

        public object EffectiveSettings => _Settings;

        public void Dispose()
        {
        }

        public void Export(IParsedVisualExpression[] allExpressions)
        {
            foreach(var img in allExpressions.OfType<ParsedImage>())
            {
                var dlg = new SaveFileDialog { Title = $"Save '{img.Name}'", DefaultExt = ".png", AddExtension = true, Filter = "PNG files|*.png" };
                if (dlg.ShowDialog() != true)
                    return;

                using (var fileStream = new FileStream(dlg.FileName, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(img.ToBitmapSource(1, 1)));
                    encoder.Save(fileStream);
                }
            }
        }
    }

    public class ImageWatchSettings
    {
        public string WidthExpression, HeightExpression;
        public string FrameBufferExpression;
        public string PixelFormat;

        public int LastKnownWidth, LastKnownHeight;

        public override string ToString()
        {
            if (LastKnownWidth > 0 && LastKnownHeight > 0)
                return $"bitmap ({LastKnownWidth}x{LastKnownHeight})";
            else
                return "bitmap";
        }

        public ImageWatchSettings Clone() => (ImageWatchSettings)MemberwiseClone();
    }
}
