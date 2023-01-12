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
        public Type PreferencesType => typeof(ImageWatchPreferences);

        public IVisualExpressionConfigurator CreateConfigurator(IVisualExpressionParsingContext ctx) => new ImageWatchConfigurator(ctx);

        public IVisualExpressionViewer CreateViewer(VisualExpressionPreferencesBase preferences) => new ImageWatchViewer(preferences as ImageWatchPreferences);

        public IParsedVisualExpression ParseExpression(IVisualExpressionParsingContext ctx)
        {
            var prefs = ctx.Preferences as ImageWatchPreferences;
            var settings = ctx.PersistentSettings as ImageWatchSettings ?? prefs?.LookupSettings(ctx)?.Clone() ?? new ImageWatchSettings();

            if (settings.PixelFormat == PixelFormatAdapters.EncodedFormatID)
            {
                if (string.IsNullOrEmpty(settings.FileSizeExpression))
                    settings = ctx.ShowModalSettingsDialog(null) as ImageWatchSettings ?? throw new OperationCanceledException();

                ulong framebufferAddress = ComputeFramebufferAddress(ctx, settings);
                int size = (int)(ctx.EvaluateSubexpression(settings.FileSizeExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the data size")).Value;

                var data = ctx.Evaluator.ReadMemory(framebufferAddress, size) ?? throw new Exception("Failed to read imag edata");

                return new ParsedImage(new TranslatedImage(data, PixelFormats.Default), settings, ctx.RawExpression);
            }
            else
            {
                if (string.IsNullOrEmpty(settings.WidthExpression) || string.IsNullOrEmpty(settings.HeightExpression))
                    settings = ctx.ShowModalSettingsDialog(null) as ImageWatchSettings ?? throw new OperationCanceledException();

                settings.LastKnownWidth = (int)(ctx.EvaluateSubexpression(settings.WidthExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the width")).Value;
                settings.LastKnownHeight = (int)(ctx.EvaluateSubexpression(settings.WidthExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the height")).Value;
                ulong framebufferAddress = ComputeFramebufferAddress(ctx, settings);

                var pixelFormat = PixelFormatAdapters.AllFormats.FirstOrDefault(f => f.ID == settings.PixelFormat) ?? throw new Exception("Unknown pixel format: " + settings.PixelFormat);
                var data = ctx.Evaluator.ReadMemory(framebufferAddress, pixelFormat.GetBufferSizeInBytes(settings.LastKnownWidth, settings.LastKnownHeight)) ?? throw new Exception("Failed to read framebuffer contents");

                if (prefs != null)
                {
                    prefs.Defaults.RemoveAll(x => x.VariableName == ctx.RawExpression);
                    prefs.Defaults.Add(new VariableDefaults { VariableName = ctx.RawExpression, TypeName = ctx.ResolvedType, Settings = settings });
                }

                return new ParsedImage(pixelFormat.Translate(data, settings.LastKnownWidth, settings.LastKnownHeight), settings, ctx.RawExpression);
            }
        }

        private static ulong ComputeFramebufferAddress(IVisualExpressionParsingContext ctx, ImageWatchSettings settings)
        {
            ulong framebufferAddress;
            if (string.IsNullOrEmpty(settings.FrameBufferExpression))
                framebufferAddress = ctx.Address ?? throw new Exception($"Could not compute address of '{ctx.RawExpression}'");
            else
                framebufferAddress = (ctx.EvaluateSubexpression(settings.FrameBufferExpression) as ExpressionValue.Integral ?? throw new Exception("Failed to evaluate the height")).Value;
            return framebufferAddress;
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
        public BitmapSource ToBitmapSource(double xScale, double yScale)
        {
            if (_Settings.PixelFormat == PixelFormatAdapters.EncodedFormatID)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(_Image.Data);
                bitmap.EndInit();
                return bitmap;
            }
            else
                return BitmapSource.Create(_Settings.LastKnownWidth, _Settings.LastKnownHeight, 96 * xScale, 96 * yScale, _Image.Format, null, _Image.Data, _Image.Data.Length / _Settings.LastKnownHeight);
        }

        public bool CanExport => true;

        public VisualExpressionSettingsBase EffectiveSettings => _Settings;

        public void Dispose()
        {
        }

        public void Export(IParsedVisualExpression[] allExpressions)
        {
            foreach (var img in allExpressions.OfType<ParsedImage>())
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

    public class ImageWatchSettings : VisualExpressionSettingsBase
    {
        public string WidthExpression, HeightExpression;
        public string FileSizeExpression;
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

    public class ImageWatchPreferences : VisualExpressionPreferencesBase
    {
        public ImageWatchViewMode ViewMode;
        public List<VariableDefaults> Defaults = new List<VariableDefaults>();
        public string DefaultPixelFormat;

        public ImageWatchSettings LookupSettings(IVisualExpressionParsingContext ctx)
        {
            return Defaults.FirstOrDefault(x => x.VariableName == ctx.RawExpression)?.Settings ?? Defaults.FirstOrDefault(x => x.TypeName == ctx.ResolvedType)?.Settings;
        }
    }

    public class VariableDefaults
    {
        public string VariableName;
        public string TypeName;

        public ImageWatchSettings Settings;
    }
}
