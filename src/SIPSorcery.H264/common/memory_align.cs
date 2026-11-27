//-----------------------------------------------------------------------------
// Filename: memory_align.cs
//
// Description: Port of:
//  - codec/common/inc/memory_align.h
//  - codec/common/src/memory_align.cpp
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// All codec state lives in native memory, exactly as upstream allocates it, so
// the ported code can keep upstream's pointer arithmetic and the GC never moves
// or scans it. malloc becomes NativeMemory.Alloc; the aligned-block layout
// (original pointer and payload size stored just below the aligned address) is
// unchanged, because CMemoryAlign::WelsFree reads the size back from there.
//
// Port additions, marked below: a process-wide count of live allocations so
// tests can assert that a codec instance frees everything it allocated.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2013, Cisco Systems
 *     All rights reserved.
 *
 *     Redistribution and use in source and binary forms, with or without
 *     modification, are permitted provided that the following conditions
 *     are met:
 *
 *        * Redistributions of source code must retain the above copyright
 *          notice, this list of conditions and the following disclaimer.
 *
 *        * Redistributions in binary form must reproduce the above copyright
 *          notice, this list of conditions and the following disclaimer in
 *          the documentation and/or other materials provided with the
 *          distribution.
 *
 *     THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 *     "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 *     LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
 *     FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
 *     COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
 *     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 *     BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 *     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 *     CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 *     LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
 *     ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 *     POSSIBILITY OF SUCH DAMAGE.
 *
 */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SIPSorcery.H264;

/// <summary>
/// Port of WelsCommon::CMemoryAlign. A plain struct allocated in native memory
/// (see <see cref="WelsCommon.WELS_NEW_CMemoryAlign"/>), referenced as
/// CMemoryAlign* the way upstream's contexts hold it. MEMORY_MONITOR is on, as
/// upstream builds it.
/// </summary>
internal unsafe struct CMemoryAlign
{
    public uint m_nCacheLineSize;
    public uint m_nMemoryUsageInBytes;   // MEMORY_MONITOR

    public CMemoryAlign(uint kuiCacheLineSize)
    {
        m_nMemoryUsageInBytes = 0;
        if ((kuiCacheLineSize == 0) || ((kuiCacheLineSize & 0x0f) != 0))
            m_nCacheLineSize = 0x10;
        else
            m_nCacheLineSize = kuiCacheLineSize;
    }

    public void* WelsMallocz(uint kuiSize, string kpTag)
    {
        void* pPointer = WelsMalloc(kuiSize, kpTag);
        if (null == pPointer)
        {
            return null;
        }
        // zero memory
        NativeMemory.Clear(pPointer, kuiSize);

        return pPointer;
    }

    public void* WelsMalloc(uint kuiSize, string kpTag)
    {
        void* pPointer = WelsCommon.WelsMalloc(kuiSize, kpTag, m_nCacheLineSize);
        if (pPointer != null)
        {
            int kiMemoryLength = *(int*)((byte*)pPointer - sizeof(void**) - sizeof(int))
                                 + (int)m_nCacheLineSize - 1 + sizeof(void**) + sizeof(int);
            m_nMemoryUsageInBytes += (uint)kiMemoryLength;
        }
        return pPointer;
    }

    public void WelsFree(void* pPointer, string kpTag)
    {
        if (pPointer != null)
        {
            int kiMemoryLength = *(int*)((byte*)pPointer - sizeof(void**) - sizeof(int))
                                 + (int)m_nCacheLineSize - 1 + sizeof(void**) + sizeof(int);
            m_nMemoryUsageInBytes -= (uint)kiMemoryLength;
        }
        WelsCommon.WelsFree(pPointer, kpTag);
    }

    public readonly uint WelsGetCacheLineSize() => m_nCacheLineSize;

    public readonly uint WelsGetMemoryUsage() => m_nMemoryUsageInBytes;
}

internal static unsafe partial class WelsCommon
{
    /// <summary>Port addition: live allocations made through WelsMalloc, for leak tests.</summary>
    private static long s_liveAllocations;

    /// <summary>Port addition: the number of WelsMalloc blocks not yet freed, process wide.</summary>
    internal static long WelsLiveAllocationCount => Interlocked.Read(ref s_liveAllocations);

    public static void* WelsMalloc(uint kuiSize, string kpTag, uint kiAlign)
    {
        uint kiSizeOfVoidPointer = (uint)sizeof(void**);
        uint kiSizeOfInt = sizeof(int);
        uint kiAlignedBytes = kiAlign - 1;
        uint kiTrialRequestedSize = kuiSize + kiAlignedBytes + kiSizeOfVoidPointer + kiSizeOfInt;
        uint kiActualRequestedSize = kiTrialRequestedSize;
        uint kiPayloadSize = kuiSize;

        byte* pBuf = (byte*)NativeMemory.Alloc(kiActualRequestedSize);
        if (null == pBuf)
            return null;

        byte* pAlignedBuffer;
        pAlignedBuffer = pBuf + kiAlignedBytes + kiSizeOfVoidPointer + kiSizeOfInt;
        pAlignedBuffer -= ((ulong)pAlignedBuffer & kiAlignedBytes);
        *((void**)(pAlignedBuffer - kiSizeOfVoidPointer)) = pBuf;
        *((int*)(pAlignedBuffer - (kiSizeOfVoidPointer + kiSizeOfInt))) = (int)kiPayloadSize;

        Interlocked.Increment(ref s_liveAllocations);
        return pAlignedBuffer;
    }

    public static void WelsFree(void* pPointer, string kpTag)
    {
        if (pPointer != null)
        {
            NativeMemory.Free(*(((void**)pPointer) - 1));
            Interlocked.Decrement(ref s_liveAllocations);
        }
    }

    public static void* WelsMallocz(uint kuiSize, string kpTag)
    {
        void* pPointer = WelsMalloc(kuiSize, kpTag, 16);
        if (null == pPointer)
        {
            return null;
        }
        NativeMemory.Clear(pPointer, kuiSize);
        return pPointer;
    }

    /// <summary>WELS_SAFE_FREE(pPtr, pTag).</summary>
    public static void WELS_SAFE_FREE<T>(ref T* pPtr, string pTag) where T : unmanaged
    {
        if (pPtr != null)
        {
            WelsFree(pPtr, pTag);
            pPtr = null;
        }
    }

    /// <summary>WELS_NEW_OP(CMemoryAlign (kuiCacheLineSize), CMemoryAlign).</summary>
    public static CMemoryAlign* WELS_NEW_CMemoryAlign(uint kuiCacheLineSize)
    {
        // Upstream news this with the C++ allocator, not WelsMalloc, so it isn't counted in its own usage.
        CMemoryAlign* p = (CMemoryAlign*)NativeMemory.Alloc((nuint)sizeof(CMemoryAlign));
        *p = new CMemoryAlign(kuiCacheLineSize);
        return p;
    }

    /// <summary>WELS_DELETE_OP(pMa) for a CMemoryAlign.</summary>
    public static void WELS_DELETE_CMemoryAlign(ref CMemoryAlign* pMa)
    {
        if (pMa != null)
        {
            // ~CMemoryAlign: assert (m_nMemoryUsageInBytes == 0) under MEMORY_MONITOR.
            Debug.Assert(pMa->m_nMemoryUsageInBytes == 0, "CMemoryAlign destroyed with memory still allocated.");
            NativeMemory.Free(pMa);
        }
        pMa = null;
    }
}
