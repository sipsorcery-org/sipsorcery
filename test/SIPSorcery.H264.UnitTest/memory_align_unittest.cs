//-----------------------------------------------------------------------------
// Filename: memory_align_unittest.cs
//
// Description: Unit tests for the port of codec/common/src/memory_align.cpp.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Xunit;

namespace SIPSorcery.H264.UnitTest;

/// <summary>Serialises the tests that read the process-wide live allocation count.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class NativeMemoryCollection
{
    public const string Name = "NativeMemory";
}

[Collection(NativeMemoryCollection.Name)]
public unsafe class memory_align_unittest
{
    [Theory]
    [InlineData(16u)]
    [InlineData(32u)]
    [InlineData(64u)]
    [InlineData(128u)]
    public void WelsMalloc_ReturnsAlignedBlockAndRecordsPayloadSize(uint align)
    {
        for (uint size = 1; size < 300; size += 37)
        {
            byte* p = (byte*)WelsCommon.WelsMalloc(size, "test", align);
            Assert.True(p != null);
            Assert.Equal(0ul, (ulong)p % align);
            // Payload size sits just below the stored original pointer, as upstream lays it out.
            Assert.Equal((int)size, *(int*)(p - sizeof(void*) - sizeof(int)));
            // The whole payload is writable.
            for (uint i = 0; i < size; i++)
            {
                p[i] = 0xAB;
            }
            WelsCommon.WelsFree(p, "test");
        }
    }

    [Fact]
    public void WelsMallocz_ZeroesThePayload()
    {
        byte* p = (byte*)WelsCommon.WelsMallocz(1000, "test");
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(0, p[i]);
        }
        WelsCommon.WelsFree(p, "test");
    }

    [Fact]
    public void WelsFree_AcceptsNull()
    {
        WelsCommon.WelsFree(null, "test");
    }

    [Fact]
    public void WelsSafeFree_FreesAndClearsThePointer()
    {
        long before = WelsCommon.WelsLiveAllocationCount;
        int* p = (int*)WelsCommon.WelsMallocz(64, "test");
        Assert.Equal(before + 1, WelsCommon.WelsLiveAllocationCount);

        WelsCommon.WELS_SAFE_FREE(ref p, "test");

        Assert.True(p == null);
        Assert.Equal(before, WelsCommon.WelsLiveAllocationCount);
    }

    [Theory]
    [InlineData(0u, 16u)]
    [InlineData(8u, 16u)]
    [InlineData(17u, 16u)]
    [InlineData(16u, 16u)]
    [InlineData(32u, 32u)]
    [InlineData(64u, 64u)]
    public void CMemoryAlign_NormalisesTheCacheLineSize(uint requested, uint expected)
    {
        CMemoryAlign* pMa = WelsCommon.WELS_NEW_CMemoryAlign(requested);
        Assert.Equal(expected, pMa->WelsGetCacheLineSize());
        WelsCommon.WELS_DELETE_CMemoryAlign(ref pMa);
        Assert.True(pMa == null);
    }

    [Fact]
    public void CMemoryAlign_TracksUsageBackToZero()
    {
        long before = WelsCommon.WelsLiveAllocationCount;
        CMemoryAlign* pMa = WelsCommon.WELS_NEW_CMemoryAlign(16);

        byte* a = (byte*)pMa->WelsMallocz(100, "a");
        byte* b = (byte*)pMa->WelsMalloc(5000, "b");
        Assert.Equal(0ul, (ulong)a % 16);
        Assert.Equal(0ul, (ulong)b % 16);

        // MEMORY_MONITOR accounting: payload + alignment slack + pointer + size header.
        uint overhead = 16 - 1 + (uint)sizeof(void*) + sizeof(int);
        Assert.Equal(100 + 5000 + 2 * overhead, pMa->WelsGetMemoryUsage());
        Assert.Equal(before + 2, WelsCommon.WelsLiveAllocationCount);

        pMa->WelsFree(a, "a");
        pMa->WelsFree(b, "b");
        Assert.Equal(0u, pMa->WelsGetMemoryUsage());
        Assert.Equal(before, WelsCommon.WelsLiveAllocationCount);

        WelsCommon.WELS_DELETE_CMemoryAlign(ref pMa);
    }
}
