using System;

namespace SCTP4CS.Utils
{
    public class ByteBuffer
    {
        private byte[] _data;

        /// <summary>
        /// Absolute
        /// </summary>
        public int positionAbsolute;
        /// <summary>
        /// Relative
        /// </summary>
        public int Position
        {
            get { return positionAbsolute - offset; }
            set { positionAbsolute = value + offset; }
        }

        /// <summary>
        /// Absolute
        /// </summary>
        public int offset;

        /// <summary>
        /// Absolute
        /// </summary>
        public int limitAbsolute;
        /// <summary>
        /// Relative
        /// </summary>
        public int Limit
        {
            get { return limitAbsolute - offset; }
            set
            {
                limitAbsolute = value + offset;
                if (positionAbsolute > limitAbsolute) positionAbsolute = limitAbsolute;
            }
        }

        public Endianness endianness = Endianness.Big;

        public ByteBuffer(byte[] buffer)
        {
            _data = buffer;
            positionAbsolute = 0;
            Limit = buffer.Length;
            offset = 0;
        }

        public ByteBuffer(byte[] buffer, int offset, int length)
        {
            _data = buffer;
            positionAbsolute = offset;
            Limit = offset + length;
            this.offset = offset;
        }

        public static ByteBuffer wrap(byte[] buffer, int offset, int length)
        {
            return new ByteBuffer(buffer, offset, length);
        }
        public static ByteBuffer wrap(byte[] buffer)
        {
            return new ByteBuffer(buffer);
        }
        public static ByteBuffer allocate(int length)
        {
            return new ByteBuffer(new byte[length], 0, length);
        }

        public byte[] Data
        {
            get { return _data; }
        }

        /// <summary>
        /// Relative
        /// </summary>
        public int Length
        {
            get { return Limit; }
        }

        public int AvailableBytes
        {
            get { return limitAbsolute - positionAbsolute; }
        }

        public int remaining()
        {
            return limitAbsolute - positionAbsolute;
        }
        public bool hasRemaining()
        {
            return limitAbsolute > positionAbsolute;
        }

        public ByteBuffer slice()
        {
            ByteBuffer b = new ByteBuffer(_data);
            b.offset = b.positionAbsolute = positionAbsolute;
            b.limitAbsolute = limitAbsolute;
            return b;
        }
        public ByteBuffer flip()
        {
            limitAbsolute = positionAbsolute;
            positionAbsolute = offset; // offset o 0?
            return this;
        }

        public void rewind()
        {
            positionAbsolute = 0;
        }

        #region PutMethods
        void UpdateDataSize(int position)
        {
            if (position > Limit) Limit = position;
        }

        public void Put(float value)
        {
            new FastBit.Float(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 4;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(double value)
        {
            new FastBit.Double(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 8;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(long value)
        {
            new FastBit.Long(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 8;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(ulong value)
        {
            new FastBit.Ulong(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 8;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(int value)
        {
            new FastBit.Int(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 4;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(uint value)
        {
            new FastBit.Uint(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 4;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(ushort value)
        {
            new FastBit.Ushort(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 2;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(int relativeOffset, ushort value)
        {
            new FastBit.Ushort(value).Write(_data, relativeOffset + offset, endianness);
            UpdateDataSize(relativeOffset + offset + 2);
        }
        public void Put(int relativeOffset, int value)
        {
            new FastBit.Int(value).Write(_data, relativeOffset + offset, endianness);
            UpdateDataSize(relativeOffset + offset + 4);
        }
        public void Put(int relativeOffset, uint value)
        {
            new FastBit.Uint(value).Write(_data, relativeOffset + offset, endianness);
            UpdateDataSize(relativeOffset + offset + 4);
        }

        public void Put(short value)
        {
            new FastBit.Short(value).Write(_data, positionAbsolute, endianness);
            positionAbsolute += 2;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(byte value)
        {
            _data[positionAbsolute] = value;
            positionAbsolute++;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(byte[] data, int offset, int length)
        {
            Buffer.BlockCopy(data, offset, _data, positionAbsolute, length);
            positionAbsolute += length;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(byte[] data)
        {
            Buffer.BlockCopy(data, 0, _data, positionAbsolute, data.Length);
            positionAbsolute += data.Length;
            UpdateDataSize(positionAbsolute);
        }

        public void Put(bool value)
        {
            _data[positionAbsolute] = (byte)(value ? 1 : 0);
            positionAbsolute++;
            UpdateDataSize(positionAbsolute);
        }
        #endregion

        #region GetMethods
        public byte GetByte()
        {
            byte res = _data[positionAbsolute];
            positionAbsolute += 1;
            return res;
        }

        public bool GetBool()
        {
            bool res = _data[positionAbsolute] > 0;
            positionAbsolute += 1;
            return res;
        }

        public ushort GetUShort()
        {
            ushort v = new FastBit.Ushort().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 2;
            return v;
        }

        public short GetShort()
        {
            short result = new FastBit.Short().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 2;
            return result;
        }

        public long GetLong()
        {
            long result = new FastBit.Long().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 8;
            return result;
        }

        public ulong GetULong()
        {
            ulong result = new FastBit.Ulong().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 8;
            return result;
        }

        public int GetInt()
        {
            int result = new FastBit.Int().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 4;
            return result;
        }
        public int GetInt(int relativeOffset)
        {
            int result = new FastBit.Int().Read(_data, offset + relativeOffset, endianness);
            return result;
        }

        public uint GetUInt()
        {
            uint result = new FastBit.Uint().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 4;
            return result;
        }
        public uint GetUInt(int relativeOffset)
        {
            uint result = new FastBit.Uint().Read(_data, offset + relativeOffset, endianness);
            return result;
        }

        public float GetFloat()
        {
            float result = new FastBit.Float().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 4;
            return result;
        }

        public double GetDouble()
        {
            double result = new FastBit.Double().Read(_data, positionAbsolute, endianness);
            positionAbsolute += 8;
            return result;
        }

        public void GetBytes(byte[] destination)
        {
            GetBytes(destination, destination.Length);
        }

        public void GetBytes(byte[] destination, int lenght)
        {
            Buffer.BlockCopy(_data, positionAbsolute, destination, 0, lenght);
            positionAbsolute += lenght;
        }
        #endregion
    }
}
