//-----------------------------------------------------------------------------
// Filename: IvfReader.cs
//
// Description: Simple IVF (Indeo Video Format) file reader for VP8 test vectors.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Dec 2024	Generated	Created for VP8 test vectors.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vpx.Net.TestVectors
{
    /// <summary>
    /// Simple reader for IVF format files containing VP8 video frames.
    /// IVF format spec: https://wiki.multimedia.cx/index.php/IVF
    /// </summary>
    public class IvfReader
    {
        public struct IvfHeader
        {
            public string Signature;      // "DKIF"
            public ushort Version;        // 0
            public ushort HeaderLength;   // 32
            public string Codec;          // "VP80", "VP81", etc.
            public ushort Width;
            public ushort Height;
            public uint FrameRate;
            public uint Scale;
            public uint FrameCount;
            public uint Reserved;
        }

        public struct IvfFrame
        {
            public uint Size;
            public ulong Timestamp;
            public byte[] Data;
        }

        public IvfHeader Header { get; private set; }
        public List<IvfFrame> Frames { get; private set; }

        public static IvfReader FromFile(string filePath)
        {
            var reader = new IvfReader();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                reader.ReadFile(br);
            }
            return reader;
        }

        private void ReadFile(BinaryReader reader)
        {
            // Read IVF header (32 bytes)
            Header = ReadHeader(reader);
            
            // Validate header
            if (Header.Signature != "DKIF")
                throw new InvalidDataException($"Invalid IVF signature: {Header.Signature}");
            
            if (!Header.Codec.StartsWith("VP8"))
                throw new InvalidDataException($"Unsupported codec: {Header.Codec}");

            // Read frames
            Frames = new List<IvfFrame>();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var frame = ReadFrame(reader);
                if (frame.Size > 0)
                {
                    Frames.Add(frame);
                }
            }
        }

        private IvfHeader ReadHeader(BinaryReader reader)
        {
            var header = new IvfHeader();
            
            // Read 32-byte header
            header.Signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            header.Version = reader.ReadUInt16();
            header.HeaderLength = reader.ReadUInt16();
            header.Codec = Encoding.ASCII.GetString(reader.ReadBytes(4));
            header.Width = reader.ReadUInt16();
            header.Height = reader.ReadUInt16();
            header.FrameRate = reader.ReadUInt32();
            header.Scale = reader.ReadUInt32();
            header.FrameCount = reader.ReadUInt32();
            header.Reserved = reader.ReadUInt32();
            
            return header;
        }

        private IvfFrame ReadFrame(BinaryReader reader)
        {
            var frame = new IvfFrame();
            
            // Check if we have enough bytes for frame header
            if (reader.BaseStream.Position + 12 > reader.BaseStream.Length)
                return frame; // End of file
            
            // Read 12-byte frame header
            frame.Size = reader.ReadUInt32();
            frame.Timestamp = reader.ReadUInt64();
            
            // Read frame data
            if (frame.Size > 0 && reader.BaseStream.Position + frame.Size <= reader.BaseStream.Length)
            {
                frame.Data = reader.ReadBytes((int)frame.Size);
            }
            
            return frame;
        }
    }
}