// from http://damieng.com/blog/2006/08/08/Calculating_CRC32_in_C_and_NET

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SIPSorcery.Sys
{
    public class Crc32 : HashAlgorithm
    {
        public const UInt32 DefaultPolynomial = 0xedb88320;
        public const UInt32 DefaultSeed = 0xffffffff;

        private UInt32 hash;
        private UInt32 seed;
        private UInt32[] table;
        private static UInt32[] defaultTable;

        public Crc32()
        {
            table = InitializeTable(DefaultPolynomial);
            seed = DefaultSeed;
            Initialize();
        }

        public Crc32(UInt32 polynomial, UInt32 seed)
        {
            table = InitializeTable(polynomial);
            this.seed = seed;
            Initialize();
        }

        public override void Initialize()
        {
            hash = seed;
        }

        protected override void HashCore(byte[] buffer, int start, int length)
        {
            hash = CalculateHash(table, hash, buffer.AsSpan(start, length));
        }

        protected
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER
            override
#else
            virtual
#endif
            void HashCore(ReadOnlySpan<byte> buffer)
        {
            hash = CalculateHash(table, hash, buffer);
        }

        protected override byte[] HashFinal()
        {
            byte[] hashBuffer = UInt32ToBigEndianBytes(~hash);
            this.HashValue = hashBuffer;
            return hashBuffer;
        }

        public override int HashSize
        {
            get { return 32; }
        }

        public static UInt32 Compute(byte[] buffer)
        {
            return Compute(buffer.AsSpan(0, buffer.Length));
        }

        public static UInt32 Compute(UInt32 seed, byte[] buffer)
        {
            return Compute(seed, buffer.AsSpan());
        }

        public static UInt32 Compute(UInt32 polynomial, UInt32 seed, byte[] buffer)
        {
            return Compute(polynomial, seed, buffer.AsSpan());
        }

        public static uint Compute(ReadOnlySpan<byte> buffer)
        {
            return ~CalculateHash(InitializeTable(DefaultPolynomial), DefaultSeed, buffer);
        }

        public static uint Compute(uint seed, ReadOnlySpan<byte> buffer)
        {
            return ~CalculateHash(InitializeTable(DefaultPolynomial), seed, buffer);
        }

        public static uint Compute(uint polynomial, uint seed, ReadOnlySpan<byte> buffer)
        {
            return ~CalculateHash(InitializeTable(polynomial), seed, buffer);
        }

        private static UInt32[] InitializeTable(UInt32 polynomial)
        {
            if (polynomial == DefaultPolynomial && defaultTable != null)
            {
                return defaultTable;
            }

            UInt32[] createTable = new UInt32[256];
            for (int i = 0; i < 256; i++)
            {
                UInt32 entry = (UInt32)i;
                for (int j = 0; j < 8; j++)
                {
                    if ((entry & 1) == 1)
                    {
                        entry = (entry >> 1) ^ polynomial;
                    }
                    else
                    {
                        entry = entry >> 1;
                    }
                }
                createTable[i] = entry;
            }

            if (polynomial == DefaultPolynomial)
            {
                defaultTable = createTable;
            }

            return createTable;
        }

        private static UInt32 CalculateHash(ReadOnlySpan<uint> table, uint seed, ReadOnlySpan<byte> buffer)
        {
            /*
            if (Sse42.IsSupported)
            {
                uint crc = Sse42.Crc32(seed, value);
            }
            */

            var crc = seed;
            for (int i = 0; i < buffer.Length; i++)
            {
                unchecked
                {
                    crc = (crc >> 8) ^ table[buffer[i] ^ (byte)(crc & 0xff)];
                }
            }
            return crc;
        }

        private byte[] UInt32ToBigEndianBytes(uint x)
        {
            var result = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(result, x);
            return result;
        }
    }
}
