using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelConverterBenchmarks;

internal static class Utils
{
    public static byte[] BitmapToBuffer(Bitmap bitmap, out int stride)
    {
        BitmapData bmpdata = null;

        try
        {
            bmpdata = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
            stride = bmpdata.Stride;
            int numbytes = stride * bitmap.Height;
            byte[] bytedata = new byte[numbytes];
            IntPtr ptr = bmpdata.Scan0;

            Marshal.Copy(ptr, bytedata, 0, numbytes);

            return bytedata;
        }
        finally
        {
            if (bmpdata != null)
            {
                bitmap.UnlockBits(bmpdata);
            }
        }
    }

    public static byte[] CreateOddDimensionBuffer(int width, int height)
    {
        var ySize = width * height;
        var uvWidth = (width + 1) / 2;
        var uvHeight = (height + 1) / 2;
        var uvSize = uvWidth * uvHeight * 2;

        return new byte[ySize + uvSize];
    }
}
