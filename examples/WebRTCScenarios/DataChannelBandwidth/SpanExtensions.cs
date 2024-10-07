using System.Buffers;

namespace DataChannelBandwidth;

static class SpanExtensions
{
    public static ArraySegment<T> ToArraySegment<T>(this ReadOnlySpan<T> span, ArrayPool<T> pool)
    {
        var result = pool.Rent(span.Length);
        span.CopyTo(result);
        return new ArraySegment<T>(result, 0, span.Length);
    }
}
