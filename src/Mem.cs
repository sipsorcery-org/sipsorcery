//-----------------------------------------------------------------------------
// Filename: Memcs
//
// Description: This class provides some helper functions for manipulating
// memory. It is intended to replicate C functions that have no corresponding
// C# counterpart.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 06 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace Vpx.Net
{
    public unsafe static class Mem
    {
        /// <summary>
        /// Sets a block of memory to a specific value.
        /// </summary>
        /// <param name="dst">The pointer to set from.</param>
        /// <param name="val">The value to set.</param>
        /// <param name="count">The count of the elements to set.</param>
        /// <remarks>
        /// Duplicates the C memset function.
        /// </remarks>
        public static void memset<T>(T* dst, T val, int count) where T : unmanaged
        {
            for(int i=0; i<count; i++)
            {
                *dst++ = val;
            }
        }

        /// <summary>
        /// Sets a section of an array to a specific value.
        /// </summary>
        /// <param name="dst">The array to set.</param>
        /// <param name="val">The value to set.</param>
        /// <param name="count">The count of the elements to set.</param>
        /// <remarks>
        /// Duplicates the C memset function.
        /// </remarks>
        public static void memset<T>(T[] dst, T val, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dst[i] = val;
            }
        }

        /// <summary>
        /// Copies a block of memory.
        /// </summary>
        /// <param name="dst">The pointer to copy the memory to.</param>
        /// <param name="src">The pointer to copy the memory from.</param>
        /// <param name="size">The number of bytes to copy.</param>
        /// <remarks>
        /// Duplicates the C memcpy function.
        /// </remarks>
        public static void memcpy(byte* dst, byte* src, int size)
        {
            Buffer.MemoryCopy(src, dst, size, size);
        }
    }
}
