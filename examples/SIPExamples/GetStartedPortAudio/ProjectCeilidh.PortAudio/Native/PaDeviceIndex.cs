using System;

namespace ProjectCeilidh.PortAudio.Native
{
    internal readonly struct PaDeviceIndex : IComparable<PaDeviceIndex>, IEquatable<PaDeviceIndex>
    {
        private readonly int _value;

        private PaDeviceIndex(int value) => _value = value;

        /// <summary>
        /// Determines if this index actually represents an error code.
        /// </summary>
        /// <param name="code">The error code that was represented.</param>
        /// <returns>True if the produced code is valid and this is actually an error, false otherwise.</returns>
        public bool TryGetErrorCode(out PaErrorCode code) => (code = (PaErrorCode)_value) < 0;

        public int CompareTo(PaDeviceIndex other) => _value.CompareTo(other._value);

        public bool Equals(PaDeviceIndex other) => this == other;

        public override bool Equals(object obj) => !(obj is PaDeviceIndex other) || !Equals(other);

        public override int GetHashCode() => _value;

        public static PaDeviceIndex operator +(PaDeviceIndex one, PaDeviceIndex two) => new PaDeviceIndex(one._value + two._value);
        public static PaDeviceIndex operator -(PaDeviceIndex one, PaDeviceIndex two) => new PaDeviceIndex(one._value - two._value);
        public static PaDeviceIndex operator ++(PaDeviceIndex one) => new PaDeviceIndex(one._value + 1);
        public static PaDeviceIndex operator --(PaDeviceIndex one) => new PaDeviceIndex(one._value - 1);
        public static bool operator <(PaDeviceIndex one, PaDeviceIndex two) => one._value < two._value;
        public static bool operator >(PaDeviceIndex one, PaDeviceIndex two) => one._value > two._value;
        public static bool operator <=(PaDeviceIndex one, PaDeviceIndex two) => one._value <= two._value;
        public static bool operator >=(PaDeviceIndex one, PaDeviceIndex two) => one._value >= two._value;
        public static bool operator ==(PaDeviceIndex one, PaDeviceIndex two) => one._value == two._value;
        public static bool operator !=(PaDeviceIndex one, PaDeviceIndex two) => one._value != two._value;

        public static explicit operator int(PaDeviceIndex index) => index._value;
    }
}
