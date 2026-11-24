//-----------------------------------------------------------------------------
// Filename: RecordingBufferWriter.cs
//
// Description: A buffer writer for tests that keeps what is written and records
// how it was asked for memory, so a packetiser's use of its writer can be checked.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 24 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>
    /// Keeps everything written in one array and records the size hint of every GetSpan or GetMemory and the count
    /// of every Advance.
    /// </summary>
    internal sealed class RecordingBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[16];

        public int WrittenCount { get; private set; }

        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, WrittenCount);

        public List<int> SizeHints { get; } = new List<int>();

        public List<int> Advances { get; } = new List<int>();

        public void Advance(int count)
        {
            if (count < 0 || WrittenCount + count > _buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            Advances.Add(count);
            WrittenCount += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Reserve(sizeHint);
            return _buffer.AsMemory(WrittenCount);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Reserve(sizeHint);
            return _buffer.AsSpan(WrittenCount);
        }

        private void Reserve(int sizeHint)
        {
            SizeHints.Add(sizeHint);

            int needed = WrittenCount + Math.Max(sizeHint, 1);
            if (needed > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(needed, 2 * _buffer.Length));
            }
        }
    }
}
