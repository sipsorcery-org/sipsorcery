using System;

namespace ProjectCeilidh.PortAudio.Native
{
    /// <summary>
    /// Used to enumerate host APIs at runtime. Values of this type range from 0 to (<see cref="PortAudio.Pa_GetHostApiCount"/>-1).
    /// </summary>
    internal readonly struct PaHostApiIndex : IComparable<PaHostApiIndex>, IEquatable<PaHostApiIndex>
    {
        private readonly int _value;

        private PaHostApiIndex(int value) => _value = value;

        /// <summary>
        /// Determines if this index actually represents an error code.
        /// </summary>
        /// <param name="code">The error code that was represented.</param>
        /// <returns>True if the produced code is valid and this is actually an error, false otherwise.</returns>
        public bool TryGetErrorCode(out PaErrorCode code) => (code = (PaErrorCode) _value) < 0;

        public int CompareTo(PaHostApiIndex other) => _value.CompareTo(other._value);

        public bool Equals(PaHostApiIndex other) => this == other;

        public override bool Equals(object obj) => !(obj is PaHostApiIndex other) || !Equals(other);

        public override int GetHashCode() => _value;

        public static PaHostApiIndex operator +(PaHostApiIndex one, PaHostApiIndex two) => new PaHostApiIndex(one._value + two._value);
        public static PaHostApiIndex operator -(PaHostApiIndex one, PaHostApiIndex two) => new PaHostApiIndex(one._value - two._value);
        public static PaHostApiIndex operator ++(PaHostApiIndex one) => new PaHostApiIndex(one._value + 1);
        public static PaHostApiIndex operator --(PaHostApiIndex one) => new PaHostApiIndex(one._value - 1);
        public static bool operator <(PaHostApiIndex one, PaHostApiIndex two) => one._value < two._value;
        public static bool operator >(PaHostApiIndex one, PaHostApiIndex two) => one._value > two._value;
        public static bool operator <=(PaHostApiIndex one, PaHostApiIndex two) => one._value <= two._value;
        public static bool operator >=(PaHostApiIndex one, PaHostApiIndex two) => one._value >= two._value;
        public static bool operator ==(PaHostApiIndex one, PaHostApiIndex two) => one._value == two._value;
        public static bool operator !=(PaHostApiIndex one, PaHostApiIndex two) => one._value != two._value;

        public static explicit operator int(PaHostApiIndex index) => index._value;
    }
}
