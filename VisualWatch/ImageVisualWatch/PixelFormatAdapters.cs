using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace ImageVisualWatch
{
    public static class PixelFormatAdapters
    {
        class WPFFormatWrapper : IPixelFormatAdapter
        {
            private readonly PixelFormat _Format;

            public WPFFormatWrapper(PixelFormat format)
            {
                _Format = format;
            }

            public string ID => "WPF:" + _Format;
            public string UserFriendlyName => _Format.ToString().ToUpper();


            public int GetBufferSizeInBytes(int width, int height) => width * height * (_Format.BitsPerPixel / 8);
            public TranslatedImage Translate(byte[] rawBuffer, int width, int height) => new TranslatedImage(rawBuffer, _Format);
        }

        class BGR24 : IPixelFormatAdapter
        {
            public string ID => "BGR24";
            public string UserFriendlyName => "BGR (24-bit)";

            public int GetBufferSizeInBytes(int width, int height) => width * height * 3;
            public TranslatedImage Translate(byte[] rawBuffer, int width, int height)
            {
                byte[] result = new byte[width * height * 4];
                for (int i = 0; i < (width * height); i++)
                {
                    result[i * 4] = rawBuffer[i * 3];
                    result[i * 4 + 1] = rawBuffer[i * 3 + 1];
                    result[i * 4 + 2] = rawBuffer[i * 3 + 2];
                    result[i * 4 + 3] = 0xFF;
                }
                return new TranslatedImage(result, PixelFormats.Bgr24);
            }
        }

        public const string EncodedFormatID = "com.sysprogs.imagewatch.encoded";

        public class EncodedFormat : IPixelFormatAdapter
        {
            public string ID => EncodedFormatID;

            public string UserFriendlyName => "PNG/JPG";

            public int GetBufferSizeInBytes(int width, int height) => throw new NotSupportedException();

            public TranslatedImage Translate(byte[] rawBuffer, int width, int height) => throw new NotSupportedException();
        }

        public static IPixelFormatAdapter[] AllFormats { get; } = new IPixelFormatAdapter[]
        {
            //new BGR24(),
            new WPFFormatWrapper(PixelFormats.Bgr32),
            new WPFFormatWrapper(PixelFormats.Bgr24),
            new WPFFormatWrapper(PixelFormats.Rgb24),
            new WPFFormatWrapper(PixelFormats.Gray8),
            new EncodedFormat(),
        };
    }

    public interface IPixelFormatAdapter
    {
        string ID { get; }
        string UserFriendlyName { get; }
        int GetBufferSizeInBytes(int width, int height);
        TranslatedImage Translate(byte[] rawBuffer, int width, int height);
    }

    public struct TranslatedImage
    {
        public byte[] Data;
        public PixelFormat Format;

        public TranslatedImage(byte[] data, PixelFormat format)
        {
            Data = data;
            Format = format;
        }
    }
}
