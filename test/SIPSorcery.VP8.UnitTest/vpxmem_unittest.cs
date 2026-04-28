//-----------------------------------------------------------------------------
// Filename: vpxmem_unittest.cs
//
// Description: Unit tests for logic in vpxmem.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Oct 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public unsafe class vpxmem_unittest
    {
        /// <summary>
        /// Tests that unmanaged memory can be allocated and released correctly.
        /// </summary>
        [Fact]
        public unsafe void MemoryAllocateTest()
        {
            void* ptr = vpx_mem.vpx_malloc(100);

            Assert.True(ptr != null && (IntPtr)ptr != IntPtr.Zero);

            vpx_mem.vpx_free(ptr);
        }

        /// <summary>
        /// Tests that unmanaged memory can be allocated with a custom alignment and then 
        /// released correctly.
        /// </summary>
        [Fact]
        public unsafe void AlignedMemoryAllocateTest()
        {
            void* ptr = vpx_mem.vpx_memalign(8, 999);

            Assert.True(ptr != null && (IntPtr)ptr != IntPtr.Zero);

            vpx_mem.vpx_free(ptr);
        }


        /// <summary>
        /// Tests that unmanaged memory can be allocatedand zeroed and then 
        /// released correctly.
        /// </summary>
        [Fact]
        public unsafe void ZeroMemoryAllocateTest()
        {
            void* ptr = vpx_mem.vpx_calloc(1000, 3);

            Assert.True(ptr != null && (IntPtr)ptr != IntPtr.Zero);

            vpx_mem.vpx_free(ptr);
        }
    }
}
