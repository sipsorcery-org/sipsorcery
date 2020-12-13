//-----------------------------------------------------------------------------
// Filename: vpx_mem.cs
//
// Description: Port of:
//  - vpx_mem.c
//  - vpx_mem.h
//  - vpx_mem_intrnl.h
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

/*
 *  Copyright (c) 2010 The WebM project authors. All Rights Reserved.
 *
 *  Use of this source code is governed by a BSD-style license
 *  that can be found in the LICENSE file in the root of the source
 *  tree. An additional intellectual property rights grant can be found
 *  in the file PATENTS.  All contributing project authors may
 *  be found in the AUTHORS file in the root of the source tree.
 */

using System;
using System.Runtime.InteropServices;

namespace Vpx.Net
{
     /// <summary>
     /// This class implements a wrapper around unmanaged memory allocation (in libvpx a wrapper
     /// around malloc etc.). The purpose of the wrapper is to ensure that the memory address
     /// supplied to the application is on an alignment boundary. 
     /// 
     /// For example if a 50 byte memory allocation with a 16 byte alignment is required then:
     ///  - 50 + 16 - 1 + 8 = 73 bytes are allocated at addr. The 8 is the the size of a pointer address.
     ///  - The next block alignment address, alignAddr, is determined from addr + 8.
     ///  - The original allocation address is stored at alignAddr - 8.
     ///  - alignAddr is returned to the caller.
     /// To free the process is reversed:
     ///  - The orignal allocation address needs to be retrieved,
     ///  - Free is called.
     /// </summary>
    public unsafe static class vpx_mem
    {
        /// <remarks>
        /// Original definition:
        /// #define ADDRESS_STORAGE_SIZE sizeof(size_t)
        /// </remarks>
        private const int ADDRESS_STORAGE_SIZE = 8;

        private static readonly ulong VPX_MAX_ALLOCABLE_MEMORY = 1UL << 40;

        /// <summary>
        /// default addr alignment to use in calls to vpx_* functions other than
        /// vpx_memalign.
        /// </summary>
        private static readonly uint DEFAULT_ALIGNMENT = (uint)(2 * sizeof(void*)); // 16.

        /// <summary>
        /// returns an addr aligned to the byte boundary specified by align.
        /// </summary>
        /// <remarks>
        /// Original definitionL
        /// #define align_addr(addr, align) \
        ///  (void*) (((size_t)(addr) + ((align)-1)) & ~(size_t)((align)-1))
        /// </remarks>
        private static void* align_addr(void* addr, ulong align)
        {
            return (void*)(((ulong)(addr) + ((align) - 1)) & ~(ulong)((align) - 1));
        }

        private static ulong* get_malloc_address_location(void* mem)
        {
            return ((ulong*)mem) - 1;
        }

        private static ulong get_aligned_malloc_size(ulong size, ulong align)
        {
            return size + align - 1 + ADDRESS_STORAGE_SIZE;
        }

        /// <remarks>
        /// Original method signature:
        /// static void set_actual_malloc_address(void *const mem, const void*const malloc_addr)
        /// </remarks>
        private static void set_actual_malloc_address(void* mem, void* malloc_addr)
        {
            void* malloc_addr_location = get_malloc_address_location(mem);
            *((ulong*)malloc_addr_location) = (ulong)malloc_addr;
        }

        private unsafe static void* get_actual_malloc_address(void* mem)
        {
            ulong* malloc_addr_location = get_malloc_address_location(mem);
            return (void*)(*malloc_addr_location);
        }

        /// <summary>
        /// Returns 0 in case of overflow of nmemb * size.
        /// </summary>
        private static int check_size_argument_overflow(ulong nmemb, ulong size)
        {
            //ulong total_size = nmemb * size;
            if (nmemb == 0) return 1;
            if (size > VPX_MAX_ALLOCABLE_MEMORY / nmemb) return 0;
            //if (total_size != (size_t)total_size) return 0;
            if (size > 0 && nmemb > ulong.MaxValue / size) return 0;

            return 1;
        }

        /// <summary>
        /// Frees unmanaged memory allocated with <seealso cref="vpx_memalign"/>,
        /// <seealso cref="vpx_malloc"/> or <seealso cref="vpx_calloc"/>. DO NOT
        /// use with memory allocated directly without using one of these three 
        /// methods.
        /// </summary>
        /// <param name="memblk">A pointer to a memory address to free and that was allocated 
        /// with <seealso cref="vpx_memalign"/>, <seealso cref="vpx_malloc"/> or 
        /// <seealso cref="vpx_calloc"/>.</param>
        public static void vpx_free(void* memblk)
        {
            if (memblk != null && (IntPtr)memblk != IntPtr.Zero)
            {
                void* addr = get_actual_malloc_address(memblk);
                Marshal.FreeHGlobal((IntPtr)addr);
            }
        }

        /// <summary>
        /// Allocate aligned unmanaged memory. The returned pointer will be aligned
        /// on an address matching the align parameter.
        /// </summary>
        /// <param name="align">The block alignment to allocated the memory for. Must
        /// be a power of 2.</param>
        /// <param name="size">The number of bytes to allocate.</param>
        /// <returns>A pointer to the start of the zeroed memory. Must be released by
        /// the caller.</returns>
        public static void* vpx_memalign(uint align, ulong size)
        {
            void* x = null, addr = null;
            ulong aligned_size = get_aligned_malloc_size(size, align);
            if (check_size_argument_overflow(1, aligned_size) == 0) return null;
            addr = (void*)Marshal.AllocHGlobal((int)aligned_size);
            if (addr != null)
            {
                x = align_addr((byte*)addr + ADDRESS_STORAGE_SIZE, align);
                set_actual_malloc_address(x, addr);
            }
            return x;
        }

        /// <summary>
        /// Allocates a new block of memory.
        /// </summary>
        /// <param name="size">The number of bytes of unmanaged memory to allocate.</param>
        /// <returns>A pointer to the start of the zeroed memory. Must be released by
        /// the caller.</returns>
        public static void* vpx_malloc(ulong size)
        {
            return vpx_memalign(DEFAULT_ALIGNMENT, size);
        }

        /// <summary>
        /// Allocates and zeroes unmanaged memory. The block of memory allocated will
        /// be num x size.
        /// </summary>
        /// <param name="num">The number of elements to allocated.</param>
        /// <param name="size">The size of each element to allocate.</param>
        /// <returns>A pointer to the start of the zeroed memory. Must be released by
        /// the caller.</returns>
        public static void* vpx_calloc(ulong num, ulong size)
        {
            void* x = null;
            if (check_size_argument_overflow(num, size) == 0) return null;

            x = vpx_malloc(num * size);
            //if (x != null) memset(x, 0, num * size);
            if (x != null)
            {
                // Note: No cross platofrm zero memoryavailable with netstandard2.0.
                // TODO: Once port complete check if this loop is a hot spot.
                for (int i = 0; i < (int)(num * size); i++)
                {
                    Marshal.WriteByte((IntPtr)x + i, 0, 0);
                }
            }
            return x;
        }
    }
}
