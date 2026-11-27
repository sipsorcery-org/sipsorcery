//-----------------------------------------------------------------------------
// Filename: VideoWindow.cs
//
// Description: The window that shows the decoded video, with a status line
// under it.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 28 Jan 2021  Aaron Clauson   Created, as part of WebRTCClientVP8Net, Dublin, Ireland.
// 27 Nov 2026  Claude          Moved into its own class for WebRTCClientCodecs.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace demo;

/// <summary>
/// Shows BGR pictures, scaled to fit, and a status line. <see cref="ShowFrame"/> and
/// <see cref="ShowStatus"/> can be called from any thread.
/// </summary>
internal sealed class VideoWindow : Form
{
    private sealed record Frame(byte[] Bgr, int Width, int Height, int Stride);

    private readonly PictureBox _picture;
    private readonly Label _status;

    // The bitmap the picture box shows, re-used until the frame size changes.
    private Bitmap? _bitmap;

    // The newest frame the UI thread hasn't drawn yet.
    private Frame? _pending;

    public VideoWindow(string status)
    {
        Text = "WebRTC Client Codecs";
        ClientSize = new Size(640, 480 + 28);

        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
        };

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(6, 0, 6, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = status,
        };

        // Docking goes from the last control added to the first, so the picture fills what the
        // status line leaves.
        Controls.Add(_picture);
        Controls.Add(_status);
    }

    /// <summary>
    /// Shows a picture. A picture that arrives before the UI thread has drawn the last one
    /// replaces it, so a slow window drops pictures rather than falling behind.
    /// </summary>
    public void ShowFrame(byte[] bgr, int width, int height, int stride)
    {
        if (Interlocked.Exchange(ref _pending, new Frame(bgr, width, height, stride)) == null && !Post(DrawPending))
        {
            _pending = null;
        }
    }

    public void ShowStatus(string status) => Post(() => _status.Text = status);

    public void CloseFromAnyThread() => Post(Close);

    private bool Post(Action action)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(action);
                return true;
            }
        }
        catch (Exception excp) when (excp is InvalidOperationException or ObjectDisposedException)
        {
            // The window is closing.
        }

        return false;
    }

    /// <summary>
    /// Copies the pending picture into the bitmap. GDI+'s Format24bppRgb is B, G, R in memory
    /// despite its name, so BGR copies in as it is.
    /// </summary>
    private void DrawPending()
    {
        var frame = Interlocked.Exchange(ref _pending, null);
        if (frame == null || IsDisposed)
        {
            return;
        }

        if (_bitmap == null || _bitmap.Width != frame.Width || _bitmap.Height != frame.Height)
        {
            var previous = _bitmap;
            _bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format24bppRgb);
            _picture.Image = _bitmap;
            previous?.Dispose();
        }

        var bmpData = _bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        try
        {
            // GDI+ pads each row to a multiple of four bytes, so the rows are copied one by one
            // unless the strides happen to agree.
            if (frame.Stride == bmpData.Stride)
            {
                Marshal.Copy(frame.Bgr, 0, bmpData.Scan0, frame.Stride * frame.Height);
            }
            else
            {
                for (int row = 0; row < frame.Height; row++)
                {
                    Marshal.Copy(frame.Bgr, row * frame.Stride, bmpData.Scan0 + row * bmpData.Stride, frame.Width * 3);
                }
            }
        }
        finally
        {
            _bitmap.UnlockBits(bmpData);
        }

        _picture.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _picture.Image = null;
            _bitmap?.Dispose();
        }

        base.Dispose(disposing);
    }
}
