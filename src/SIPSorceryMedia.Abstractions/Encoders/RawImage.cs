//-----------------------------------------------------------------------------
// Filename: RawImage.cs
//
// Description: A raw image for use with a video codec.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 May 2025  Aaron Clauson   Refactored from MediaEndPoints.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.Abstractions;

public class RawImage
{
    /// <summary>
    /// The width, in pixels, of the image
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// The height, in pixels, of the image
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Integer that specifies the byte offset between the beginning of one scan line and the next.
    /// </summary>
    public int Stride { get; set; }

    /// <summary>
    /// Pointer to an array of bytes that contains the pixel data.
    /// </summary>
    /// <remarks>
    /// WARNING: When supplied by a decoder or capture source this points at memory owned by that component
    /// and re-used for every frame, so it is only valid for the duration of the event handler it
    /// was delivered to. If the sample is needed after the handler returns, for example to display
    /// it on a UI thread, copy it first with <see cref="GetBuffer"/> or <see cref="CopyTo(byte[])"/>.
    /// Note that <see cref="CopyTo(IntPtr, int)"/> does not help there. It avoids the managed array
    /// but still has to run inside the handler.
    /// </remarks>
    public IntPtr Sample { get; set; }

    /// <summary>
    /// The pixel format of the image
    /// </summary>
    public VideoPixelFormatsEnum PixelFormat { get; set; }

    /// <summary>
    /// The number of bytes needed to hold a copy of the pixel data, or 0 if this image has no
    /// usable dimensions.
    /// </summary>
    public int BufferSize => (Height > 0 && Stride > 0) ? Height * Stride : 0;

    /// <summary>
    /// Copies the pixel data into a new managed array.
    /// </summary>
    /// <returns>The copied pixel data, or null if this image has no usable dimensions.</returns>
    /// <remarks>
    /// <para>
    /// This is the safe way to consume a decoded frame. <see cref="Sample"/> points at memory owned
    /// by the decoder and is only valid for the duration of the callback, so a handler that does
    /// anything after it returns, such as marshalling to a UI thread, must take a copy first. The
    /// returned array has no such restriction and can be held for as long as needed.
    /// </para>
    /// <para>
    /// The copy costs a fraction of a millisecond even at high resolutions, but allocates a new
    /// array per frame. Use <see cref="CopyTo(byte[])"/> with a re-used buffer to avoid that, or
    /// <see cref="Sample"/> directly when the frame is fully consumed before the handler returns.
    /// </para>
    /// </remarks>
    public byte[] GetBuffer()
    {
        byte[] result = null;

        if (BufferSize > 0)
        {
            result = new byte[BufferSize];
            Marshal.Copy(Sample, result, 0, BufferSize);
        }
        return result;
    }

    /// <summary>
    /// Copies the pixel data into a buffer supplied by the caller, so a buffer can be re-used
    /// across frames rather than allocating one per frame as <see cref="GetBuffer"/> does.
    /// </summary>
    /// <param name="destination">
    /// The buffer to copy into. Must be at least <see cref="BufferSize"/> bytes long.
    /// </param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="destination"/> is smaller than <see cref="BufferSize"/>.
    /// </exception>
    public int CopyTo(byte[] destination)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        var bufferSize = BufferSize;

        if (bufferSize == 0)
        {
            return 0;
        }

        if (destination.Length < bufferSize)
        {
            throw new ArgumentException($"Destination buffer of {destination.Length} bytes is too small for a {Width}x{Height} image needing {bufferSize} bytes.", nameof(destination));
        }

        Marshal.Copy(Sample, destination, 0, bufferSize);

        return bufferSize;
    }

    /// <summary>
    /// Copies the pixel data straight into unmanaged memory, such as the Scan0 of a locked bitmap,
    /// with no intermediate managed array.
    /// </summary>
    /// <param name="destination">The destination buffer, which must hold at least <see cref="BufferSize"/> bytes.</param>
    /// <returns>The number of bytes copied.</returns>
    /// <remarks>
    /// Only valid while <see cref="Sample"/> is, which is for the duration of the callback that
    /// supplied this image. See the overload taking a destination stride for the threading caveat.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is a null pointer.</exception>
    public int CopyTo(IntPtr destination) => CopyTo(destination, Stride);

    /// <summary>
    /// Copies the pixel data straight into unmanaged memory whose rows are a different length to
    /// this image's, such as a GDI+ bitmap that pads each row to a multiple of four bytes.
    /// </summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="destinationStride">The byte offset between the start of one destination row and the next.</param>
    /// <returns>The number of bytes copied.</returns>
    /// <remarks>
    /// <para>
    /// This is the zero allocation path: one copy, decoder buffer straight to destination. It is
    /// only usable while <see cref="Sample"/> is valid, meaning inside the callback that supplied
    /// this image, so the destination has to be ready at that point.
    /// </para>
    /// <para>
    /// Take care when the destination belongs to a UI toolkit. Writing to a bitmap from the decoder's
    /// thread while the UI thread paints that same bitmap is a data race, and GDI+ in particular
    /// throws rather than tearing. Either render synchronously, or copy into memory the UI owns
    /// exclusively, or use <see cref="GetBuffer"/> / <see cref="CopyTo(byte[])"/> and defer.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is a null pointer.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="destinationStride"/> is not greater than zero.
    /// </exception>
    /// <example>
    /// <para>
    /// The example ShowFrame method below copies a decoded 24 bits per pixel frame into a bitmap the calling
    /// application owns and displays it.
    /// </para>
    /// <para>
    /// The copy runs on the decoder's callback thread, the only place <see cref="Sample"/> is
    /// guaranteed to be valid.
    /// </para>
    /// <para>
    /// That thread also carries the media transport, so it needs to do the minimum amount of work
    /// possible. Anything slow on it holds up the packets still arriving.
    /// </para>
    /// <para>
    /// The mechanism used is to rotate two bitmaps so the one being written is never the one the picture
    /// box is painting, which GDI+ would throw on: the UI thread hands the previous bitmap back once it has been
    /// replaced and it will be used for the next frame copy.
    /// </para>
    /// <para>
    /// The bitmaps are allocated once and re-used until the frame size changes. Allocating one per
    /// frame costs several times more than the copy itself and, at 1080p and above, produces enough
    /// garbage to stall the application.
    /// </para>
    /// <para>
    /// Note GDI+'s Format24bppRgb is B,G,R in memory despite the name, which is why a Bgr sample
    /// can be copied into it verbatim.
    /// </para>
    /// <para>
    /// _spareBmp, _form, _picBox and logger are fields of the calling form.
    /// </para>
    /// <code>
    /// videoSink.OnVideoSinkDecodedSampleFaster += ShowFrame;
    ///
    /// private static void ShowFrame(RawImage rawImage)
    /// {
    ///     if (rawImage.PixelFormat != VideoPixelFormatsEnum.Bgr)
    ///     {
    ///         logger.LogError($"Cannot display decoded video sample, expected pixel format Bgr but got {rawImage.PixelFormat}.");
    ///         return;
    ///     }
    ///
    ///     var bmp = Interlocked.Exchange(ref _spareBmp, null);
    ///
    ///     int width = rawImage.Width;
    ///     int height = rawImage.Height;
    ///
    ///     if (bmp == null || bmp.Width != width || bmp.Height != height)
    ///     {
    ///         bmp?.Dispose();
    ///         bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
    ///     }
    ///
    ///     var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
    ///
    ///     try
    ///     {
    ///         rawImage.CopyTo(bmpData.Scan0, bmpData.Stride);
    ///     }
    ///     finally
    ///     {
    ///         bmp.UnlockBits(bmpData);
    ///     }
    ///
    ///     _form.BeginInvoke(new Action(() =>
    ///     {
    ///         if (_picBox.Width != width || _picBox.Height != height)
    ///         {
    ///             logger.LogDebug($"Adjusting video display to {width}x{height}.");
    ///             _picBox.Width = width;
    ///             _picBox.Height = height;
    ///         }
    ///
    ///         var previous = _picBox.Image as Bitmap;
    ///         _picBox.Image = bmp;
    ///
    ///         // The previous picture box bitmap is no longer being painted so it can be
    ///         // used for the next frame copy.
    ///         Interlocked.Exchange(ref _spareBmp, previous)?.Dispose();
    ///     }));
    /// }
    /// </code>
    /// </example>
    public unsafe int CopyTo(IntPtr destination, int destinationStride)
    {
        if (destination == IntPtr.Zero)
        {
            throw new ArgumentException("Destination cannot be a null pointer.", nameof(destination));
        }

        if (BufferSize == 0)
        {
            return 0;
        }

        if (destinationStride <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationStride), "Destination stride must be greater than zero.");
        }

        // A row may be shorter at either end: the decoder can pad its rows, and so can the
        // destination. Only the bytes present in both are meaningful.
        int rowBytes = Math.Min(Stride, destinationStride);

        if (Stride == destinationStride)
        {
            // No padding to step over, so the whole image goes in one copy.
            Buffer.MemoryCopy((void*)Sample, (void*)destination, (long)destinationStride * Height, (long)Stride * Height);
        }
        else
        {
            for (int row = 0; row < Height; row++)
            {
                Buffer.MemoryCopy((byte*)Sample + (long)row * Stride, (byte*)destination + (long)row * destinationStride, destinationStride, rowBytes);
            }
        }

        return rowBytes * Height;
    }
}
