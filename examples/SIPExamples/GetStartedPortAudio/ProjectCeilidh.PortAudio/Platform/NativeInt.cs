using System;

namespace ProjectCeilidh.PortAudio.Platform
{
    /// <summary>
    /// Represents a signed integer with the native system size.
    /// Good for representing size_t and other fields with platform-dependent size that aren't pointers.
    /// </summary>
    internal readonly unsafe struct NativeInt
    {
        private readonly IntPtr _value;

        public NativeInt(IntPtr value)
        {
            _value = value;
        }

        public NativeInt(int value)
        {
            _value = new IntPtr(value);
        }

        public NativeInt(long value)
        {
            if (IntPtr.Size < sizeof(long) && value > int.MaxValue) throw new NotSupportedException("Creating a NativeInt from a long this large will truncate on this platform.");

            _value = new IntPtr(value);
        }

        public NativeInt(void* value)
        {
            _value = new IntPtr(value);
        }

        public static implicit operator IntPtr(NativeInt value) => value._value;
        public static implicit operator NativeInt(IntPtr value) => new NativeInt(value);
        public static implicit operator NativeInt(int value) => new NativeInt(value);
        public static implicit operator NativeInt(void* value) => new NativeInt(value);

        public static explicit operator NativeInt(long value) => new NativeInt(value);

        public static explicit operator int(NativeInt value) => value._value.ToInt32();
        public static explicit operator long(NativeInt value) => value._value.ToInt64();
        public static explicit operator void*(NativeInt value) => value._value.ToPointer();
    }

    /// <summary>
    /// Represents an unsigned integer with the native system size.
    /// Good for representing size_t and other fields with platform-dependent size that aren't pointers.
    /// </summary>
    internal readonly unsafe struct NativeUInt
    {
        private readonly UIntPtr _value;

        public NativeUInt(UIntPtr value)
        {
            _value = value;
        }

        public NativeUInt(uint value)
        {
            _value = new UIntPtr(value);
        }

        public NativeUInt(ulong value)
        {
            if (IntPtr.Size < sizeof(ulong) && value > uint.MaxValue) throw new NotSupportedException("Creating a NativeUInt from a ulong this large will truncate on this platform.");

            _value = new UIntPtr(value);
        }

        public NativeUInt(void* value)
        {
            _value = new UIntPtr(value);
        }

        public static implicit operator UIntPtr(NativeUInt value) => value._value;
        public static implicit operator NativeUInt(UIntPtr value) => new NativeUInt(value);
        public static implicit operator NativeUInt(uint value) => new NativeUInt(value);
        public static implicit operator NativeUInt(void* value) => new NativeUInt(value);

        public static explicit operator NativeUInt(ulong value) => new NativeUInt(value);

        public static explicit operator uint(NativeUInt value) => value._value.ToUInt32();
        public static explicit operator ulong(NativeUInt value) => value._value.ToUInt64();
        public static explicit operator void* (NativeUInt value) => value._value.ToPointer();
    }
}
