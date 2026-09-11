using Xunit;

// The VP8 unit tests are NOT safe to run in parallel:
//
//  1. ImgHelper / several tests use System.Drawing (GDI+), which is not
//     thread-safe and throws "A generic error occurred in GDI+"
//     (ExternalException) when bitmaps are manipulated concurrently.
//  2. The libvpx port (Vpx.Net) is a direct C-to-C# translation that makes
//     heavy use of `unsafe` pointer code and shared native-style buffers.
//     Running it across multiple threads can corrupt the managed heap and
//     bring the whole test host down with "Internal CLR error (0x80131506)".
//
// xUnit parallelises distinct test collections (classes) by default, which is
// what produced the 2-3 sporadic failures / host crashes. Serialising the
// whole assembly removes the race.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
