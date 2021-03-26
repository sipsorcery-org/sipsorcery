//-----------------------------------------------------------------------------
// Filename: Utilities.cs
//
// Description: Useful functions for VoIP protocol implementation.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 May 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;

namespace SIPSorcery.Sys
{
    public class NetConvert
    {
        public static UInt16 DoReverseEndian(UInt16 x)
        {
            //return Convert.ToUInt16((x << 8 & 0xff00) | (x >> 8));
            return BitConverter.ToUInt16(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
        }

        public static uint DoReverseEndian(uint x)
        {
            //return (x << 24 | (x & 0xff00) << 8 | (x & 0xff0000) >> 8 | x >> 24);
            return BitConverter.ToUInt32(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
        }

        public static ulong DoReverseEndian(ulong x)
        {
            //return (x << 56 | (x & 0xff00) << 40 | (x & 0xff0000) << 24 | (x & 0xff000000) << 8 | (x & 0xff00000000) >> 8 | (x & 0xff0000000000) >> 24 | (x & 0xff000000000000) >> 40 | x >> 56);
            return BitConverter.ToUInt64(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
        }

        public static int DoReverseEndian(int x)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(x).Reverse().ToArray(), 0);
        }

        /// <summary>
        /// Parse a UInt16 from a network buffer using network byte order.
        /// </summary>
        /// <param name="buffer">The buffer to parse the value from.</param>
        /// <param name="posn">The position in the buffer to start the parse from.</param>
        /// <returns>A UInt16 value.</returns>
        public static ushort ParseUInt16(byte[] buffer, int posn)
        {
            return (ushort)(buffer[posn] << 8 | buffer[posn + 1]);
        }

        /// <summary>
        /// Parse a UInt32 from a network buffer using network byte order.
        /// </summary>
        /// <param name="buffer">The buffer to parse the value from.</param>
        /// <param name="posn">The position in the buffer to start the parse from.</param>
        /// <returns>A UInt32 value.</returns>
        public static uint ParseUInt32(byte[] buffer, int posn)
        {
            return (uint)(buffer[posn] << 24 | buffer[posn + 1] << 16 | buffer[posn + 2] << 8 | buffer[posn + 3]);
        }

        /// <summary>
        /// Parse a UInt64 from a network buffer using network byte order.
        /// </summary>
        /// <param name="buffer">The buffer to parse the value from.</param>
        /// <param name="posn">The position in the buffer to start the parse from.</param>
        /// <returns>A UInt64 value.</returns>
        public static ulong ParseUInt64(byte[] buffer, int posn)
        {
            return 
                 (ulong)buffer[posn] << 56 |
                 (ulong)buffer[posn + 1] << 48 |
                 (ulong)buffer[posn + 2] << 40 |
                 (ulong)buffer[posn + 3] << 32 |
                 (ulong)buffer[posn + 4] << 24 |
                 (ulong)buffer[posn + 5] << 16 |
                 (ulong)buffer[posn + 6] << 8 |
                 (ulong)buffer[posn + 7];
        }

        /// <summary>
        /// Writes a UInt16 value to a network buffer using network byte order.
        /// </summary>
        /// <param name="val">The value to write to the buffer.</param>
        /// <param name="buffer">The buffer to write the value to.</param>
        /// <param name="posn">The start position in the buffer to write the value at.</param>
        public static void ToBuffer(ushort val, byte[] buffer, int posn)
        {
            if(buffer.Length < posn + 2)
            {
                throw new ApplicationException("Buffer was too short for ushort ToBuffer.");
            }

            buffer[posn] = (byte)(val >> 8);
            buffer[posn + 1] = (byte)val;
        }

        /// <summary>
        /// Get a buffer representing the unsigned 16 bit integer in network
        /// byte (big endian) order.
        /// </summary>
        /// <param name="val">The value to convert.</param>
        /// <returns>A buffer representing the value in network order </returns>
        public static byte[] GetBytes(ushort val)
        {
            var buffer = new byte[2];
            ToBuffer(val, buffer, 0);
            return buffer;
        }

        /// <summary>
        /// Writes a UInt32 value to a network buffer using network byte order.
        /// </summary>
        /// <param name="val">The value to write to the buffer.</param>
        /// <param name="buffer">The buffer to write the value to.</param>
        /// <param name="posn">The start position in the buffer to write the value at.</param>
        public static void ToBuffer(uint val, byte[] buffer, int posn)
        {
            if (buffer.Length < posn + 4)
            {
                throw new ApplicationException("Buffer was too short for uint ToBuffer.");
            }

            buffer[posn] = (byte)(val >> 24);
            buffer[posn + 1] = (byte)(val >> 16);
            buffer[posn + 2] = (byte)(val >> 8);
            buffer[posn + 3] = (byte)val;
        }

        /// <summary>
        /// Get a buffer representing the 32 bit unsigned integer in network
        /// byte (big endian) order.
        /// </summary>
        /// <param name="val">The value to convert.</param>
        /// <returns>A buffer representing the value in network order </returns>
        public static byte[] GetBytes(uint val)
        {
            var buffer = new byte[4];
            ToBuffer(val, buffer, 0);
            return buffer;
        }

        /// <summary>
        /// Writes a UInt64 value to a network buffer using network byte order.
        /// </summary>
        /// <param name="val">The value to write to the buffer.</param>
        /// <param name="buffer">The buffer to write the value to.</param>
        /// <param name="posn">The start position in the buffer to write the value at.</param>
        public static void ToBuffer(ulong val, byte[] buffer, int posn)
        {
            if (buffer.Length < posn + 8)
            {
                throw new ApplicationException("Buffer was too short for ulong ToBuffer.");
            }

            buffer[posn] = (byte)(val >> 56);
            buffer[posn + 1] = (byte)(val >> 48);
            buffer[posn + 2] = (byte)(val >> 40);
            buffer[posn + 3] = (byte)(val >> 32);
            buffer[posn + 4] = (byte)(val >> 24);
            buffer[posn + 5] = (byte)(val >> 16);
            buffer[posn + 6] = (byte)(val >> 8);
            buffer[posn + 7] = (byte)val;
        }

        /// <summary>
        /// Get a buffer representing the 64 bit unsigned integer in network
        /// byte (big endian) order.
        /// </summary>
        /// <param name="val">The value to convert.</param>
        /// <returns>A buffer representing the value in network order </returns>
        public static byte[] GetBytes(ulong val)
        {
            var buffer = new byte[8];
            ToBuffer(val, buffer, 0);
            return buffer;
        }

        /// <summary>
        /// Reverses the endianness of a UInt32.
        /// </summary>
        /// <param name="val">The value to flip.</param>
        /// <returns>The same value but with the endianness flipped.</returns>
        public static uint EndianFlip(uint val)
        {
            return val << 24 | val << 8 & 0xff0000 | val >> 8 & 0xff00 | val >> 24;
        }
    }
}
