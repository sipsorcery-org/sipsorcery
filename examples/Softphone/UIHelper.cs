//-----------------------------------------------------------------------------
// Filename: UIHelper.cs
//
// Description: This class contains some helper and utility methods for WPF 
// applications.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Jan 2015	Aaron Clauson	Added to softphone project, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SIPSorcery.SoftPhone
{
    public static class UIHelper
    {
        private const int MOUSE_INPUT = 0;
        private const int MOUSEEVENTF_MOVE = 0x0001;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        internal struct INPUT
        {
            public int TYPE;

            // This is supposed to be a union for all input types, but we only want mouse so...
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        public static void DoOnUIThread(this Dispatcher dispatcher, Action action)
        {
            dispatcher.Invoke(DispatcherPriority.Render, (SendOrPostCallback)delegate { action(); }, null);
        }

        /// <summary>
        /// Retrieve the time of the last user interaction
        /// </summary>
        /// <returns></returns>
        public static TimeSpan GetLastInput()
        {
            var plii = new LASTINPUTINFO();
            plii.cbSize = (uint)Marshal.SizeOf(plii);

            if (GetLastInputInfo(ref plii))
            {
                return TimeSpan.FromMilliseconds(Environment.TickCount - plii.dwTime);
            }
            else
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        /// <summary>
        /// Reset the last input timer. We move the mouse one pixel to reset the system timer.
        /// </summary>
        public static void ResetIdleTimer()
        {
            INPUT input = new INPUT();
            input.TYPE = MOUSE_INPUT;
            input.dx = 1;
            input.dy = 1;
            input.mouseData = 0;
            input.dwFlags = MOUSEEVENTF_MOVE;
            input.time = 0;
            input.dwExtraInfo = (IntPtr)0;

            if (SendInput(1, ref input, System.Runtime.InteropServices.Marshal.SizeOf(input)) != 1)
            {
                throw new Win32Exception();
            }
        }

        public static byte[] LoadImageBytes(Uri imageURI)
        {
            if (imageURI != null)
            {
                var streamInfo = Application.GetResourceStream(imageURI);
                var ms = new MemoryStream();
                var imgBytes = new byte[streamInfo.Stream.Length];
                using (var stream = streamInfo.Stream)
                {
                    stream.Read(imgBytes, 0, (int)stream.Length);
                }

                return imgBytes;
            }

            return null;
        }

        public static System.Drawing.Bitmap LoadBitmap(string imageURI)
        {
            return LoadBitmap(new Uri(imageURI, UriKind.Absolute));
        }

        public static System.Drawing.Bitmap LoadBitmap(Uri imageURI)
        {
            var bmp = new BitmapImage(imageURI);

            int height = bmp.PixelHeight;
            int width = bmp.PixelWidth;
            int stride = width * ((bmp.Format.BitsPerPixel + 7) / 8);
            byte[] bits = new byte[height * stride];
            bmp.CopyPixels(bits, stride, 0);

            unsafe
            {
                fixed (byte* pBits = bits)
                {
                    IntPtr ptr = new IntPtr(pBits);

                    System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(
                        width,
                        height,
                        stride,
                        System.Drawing.Imaging.PixelFormat.Format32bppPArgb,
                        ptr);

                    return bitmap;
                }
            }
        }

        /// <summary>
        /// Converts supplied screen coordinates into the correct WPF device-independent coordinates using an
        /// existing control or window
        /// </summary>
        /// <param name="visual">The control or window the screen point is in</param>
        /// <param name="screenPoint">the point in screen coordinates to be transformed</param>
        /// <returns>new point representing the screen coordinates in WPF coordinates</returns>
        public static Point ScreenCoordinatesToWPF(Visual visual, Point screenPoint)
        {
            var t = PresentationSource.FromVisual(visual).CompositionTarget.TransformFromDevice;
            return t.Transform(screenPoint);
        }

        public static BitmapImage GetBitmapImageFromBase64String(string base64Bitmap)
        {
            BitmapImage scannedImage = new BitmapImage();
            scannedImage.BeginInit();
            scannedImage.StreamSource = new MemoryStream(Convert.FromBase64String(base64Bitmap));
            scannedImage.EndInit();

            return scannedImage;
        }

        public static string GetJPEGBase64StringFromBitmapImage(BitmapImage bmpImage, long jpegEncoderQuality, System.Drawing.RotateFlipType rotateFlip)
        {
            System.Drawing.Bitmap bmp = null;
            MemoryStream ms = new MemoryStream();

            try
            {
                System.Drawing.Imaging.EncoderParameters encoderParameters = new System.Drawing.Imaging.EncoderParameters(1);
                System.Drawing.Imaging.Encoder myEncoder = System.Drawing.Imaging.Encoder.Quality;
                encoderParameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(myEncoder, jpegEncoderQuality);
                System.Drawing.Imaging.ImageCodecInfo jgpEncoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);

                bmp = UIHelper.BitmapImage2Bitmap(bmpImage);
                bmp.RotateFlip(rotateFlip);
                bmp.Save(ms, jgpEncoder, encoderParameters);

                return Convert.ToBase64String(ms.ToArray());
            }
            finally
            {
                DeleteObject(bmp.GetHbitmap());
                bmp.Dispose();
                bmp = null;
            }
        }

        public static System.Drawing.Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(outStream);

                return new System.Drawing.Bitmap(bitmap);
            }
        }

        public static BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            BitmapSource bmpSource;
            BitmapImage bitmapImage = new BitmapImage();

            try
            {
                bmpSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                             hBitmap,
                             IntPtr.Zero,
                             Int32Rect.Empty,
                             BitmapSizeOptions.FromEmptyOptions());

                BitmapEncoder enc = new BmpBitmapEncoder();
                MemoryStream memoryStream = new MemoryStream();

                enc.Frames.Add(BitmapFrame.Create(bmpSource));
                enc.Save(memoryStream);

                memoryStream.Position = 0;
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
            }
            finally
            {
                DeleteObject(hBitmap);
            }

            return bitmapImage;
        }

        private static System.Drawing.Imaging.ImageCodecInfo GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            System.Drawing.Imaging.ImageCodecInfo[] codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageDecoders();

            foreach (System.Drawing.Imaging.ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}
