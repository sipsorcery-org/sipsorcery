// The middle tile: the frames SIPSorcery decoded, as decoded, so a fault can be placed on one
// side of the decoder or the other. They're written into memory shared with the page (a WebView2
// shared buffer), which draws the newest one with WebGL on its next animation frame: no copy
// through messages, and no encode for the page.
//
// Layout (little endian): int seq at 0, int slot at 4, then for each slot at 16 + slot * 12 its
// width, height and frame number (-1 if unread); the slots themselves from 64, each a tightly
// packed BGR frame. The decoder writes the slot after the newest one and then moves seq on, so
// the page never reads a slot while it's being written (it would have to fall two frames behind).

using Microsoft.Web.WebView2.Core;

namespace SIPSorcery.TranscodeLoop;

internal sealed unsafe class DecodedView : IDisposable
{
    public const int HeaderBytes = 64;
    public const int Slots = 3;

    /// <summary>
    /// A slot's size for frames of this size, with room for 16 more rows: a decoder may give
    /// 1088 rows for 1080 (H264 codes whole 16x16 macroblocks).
    /// </summary>
    public static long SlotBytesFor(int width, int height) => (long)width * (height + 16) * 3;

    public long SlotBytes { get; }

    private readonly CoreWebView2SharedBuffer _shared;
    private readonly byte* _base;
    private int _seq, _latest = Slots - 1;

    /// <summary>For frames up to this size. On the UI thread.</summary>
    public DecodedView(CoreWebView2Environment env, int width, int height)
    {
        SlotBytes = SlotBytesFor(width, height);
        _shared = env.CreateSharedBuffer((ulong)(HeaderBytes + Slots * SlotBytes));
        _base = (byte*)_shared.Buffer;
        new Span<byte>(_base, HeaderBytes).Clear();
    }

    /// <summary>Hands the buffer to the page (again after each load). On the UI thread.</summary>
    public void PostTo(CoreWebView2 core) =>
        core.PostSharedBufferToScript(_shared, CoreWebView2SharedBufferAccess.ReadOnly,
            $"{{\"header\":{HeaderBytes},\"slots\":{Slots},\"slotBytes\":{SlotBytes}}}");

    /// <summary>Writes a frame for the page. From the decode thread only. False if it's too big.</summary>
    public bool Write(byte[] bgr, int width, int height, int number)
    {
        long bytes = (long)width * height * 3;
        if (bytes > SlotBytes || bgr.Length < bytes) return false;
        int slot = (_latest + 1) % Slots;
        fixed (byte* src = bgr)
        {
            Buffer.MemoryCopy(src, _base + HeaderBytes + slot * SlotBytes, SlotBytes, bytes);
        }
        int* meta = (int*)(_base + 16 + slot * 12);
        meta[0] = width;
        meta[1] = height;
        meta[2] = number;
        Volatile.Write(ref *(int*)(_base + 4), slot);
        _latest = slot;
        Volatile.Write(ref *(int*)_base, ++_seq);
        return true;
    }

    public void Dispose() => _shared.Dispose();
}
