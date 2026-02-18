using System;
using System.Runtime.InteropServices;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SIPSorceryMedia.Abstractions;

namespace PixelConvertTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Pixel Convert Test Console:");

            //StreamReader sr = new StreamReader("ref-bgra32.bmp");
            var img = Image.Load("ref-bgra32.bmp");
            int width = img.Width;
            int height = img.Height;

            var refImage = img.CloneAs<Rgba32>();
            //var refImage = img.CloneAs<Rgb24>();
            //bgra32Img.SaveAsPng("ref-bgra32.png");

            if (refImage.TryGetSinglePixelSpan(out var pixelSpan))
            {
                byte[] rgba = MemoryMarshal.AsBytes(pixelSpan).ToArray();

                //var i420 = PixelConverter.RGBAtoYUV420Planar(rgba, width, height);
                var i420 = PixelConverter.RGBAtoI420(rgba, width, height);
                //var i420 = PixelConverter.RGBtoI420(rgb, width, height);
                var rgbRndTrip = PixelConverter.I420toRGB(i420, width, height);

                //Console.WriteLine($"{width}x{height}");

                //byte[] flipped = new byte[rgbRndTrip.Length];
                //int pixelSize = 3;
                //int posn = 0;
                //for (int row = height - 1; row >= 0; row--)
                //{
                //    Console.WriteLine($"row start {row * width}, posn {posn}");

                //    for (int col = 0; col < width * pixelSize; col++)
                //    {
                //        flipped[posn++] = rgbRndTrip[row * width * pixelSize + col];
                //    }
                //}

                using (var imgRndTrip = Image.LoadPixelData<Rgb24>(rgbRndTrip, width, height))
                //using (var imgRndTrip = Image.LoadPixelData<Rgb24>(flipped, width, height))
                {
                    imgRndTrip.SaveAsPng("rndtrip-rgb24-flipped.png");
                }
            }

            Console.WriteLine("Finished.");
        }
    }
}
