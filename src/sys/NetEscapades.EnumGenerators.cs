using System;
using System.Collections.Generic;
using System.Text;

#if !(NETCOREAPP && !NETCOREAPP2_0 && !NETCOREAPP1_1 && !NETCOREAPP1_0)
namespace SIPSorcery.Net
{
    static partial class IceRolesEnumExtensions
    {
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.IceRolesEnum value,
            bool ignoreCase)
            => TryParse(name, out value, ignoreCase, false);

        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.IceRolesEnum result,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => ignoreCase
                    ? TryParseIgnoreCase(in name, out result, allowMatchingMetadataAttribute)
                    : TryParseWithCase(in name, out result, allowMatchingMetadataAttribute);

        private static bool TryParseIgnoreCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.IceRolesEnum result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.IceRolesEnum.actpass).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.IceRolesEnum.actpass;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.IceRolesEnum.passive).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.IceRolesEnum.passive;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.IceRolesEnum.active).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.IceRolesEnum.active;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.IceRolesEnum)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseWithCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.IceRolesEnum result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.IceRolesEnum.actpass).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.IceRolesEnum.actpass;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.IceRolesEnum.passive).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.IceRolesEnum.passive;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.IceRolesEnum.active).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.IceRolesEnum.active;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.IceRolesEnum)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }

    static partial class STUNProtocolsEnumExtensions
    {
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNProtocolsEnum value,
            bool ignoreCase)
            => TryParse(name, out value, ignoreCase, false);

        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNProtocolsEnum result,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => ignoreCase
                    ? TryParseIgnoreCase(in name, out result, allowMatchingMetadataAttribute)
                    : TryParseWithCase(in name, out result, allowMatchingMetadataAttribute);

        private static bool TryParseIgnoreCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNProtocolsEnum result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.udp).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.udp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.tcp).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.tcp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.tls).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.tls;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.dtls).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.dtls;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.STUNProtocolsEnum)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseWithCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNProtocolsEnum result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.udp).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.udp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.tcp).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.tcp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.tls).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.tls;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNProtocolsEnum.dtls).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNProtocolsEnum.dtls;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.STUNProtocolsEnum)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }

    static partial class STUNSchemesEnumExtensions
    {
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNSchemesEnum value,
            bool ignoreCase)
            => TryParse(name, out value, ignoreCase, false);

        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNSchemesEnum result,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => ignoreCase
                    ? TryParseIgnoreCase(in name, out result, allowMatchingMetadataAttribute)
                    : TryParseWithCase(in name, out result, allowMatchingMetadataAttribute);

        private static bool TryParseIgnoreCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNSchemesEnum result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.stun).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.stun;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.stuns).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.stuns;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.turn).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.turn;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.turns).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.turns;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.STUNSchemesEnum)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseWithCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.STUNSchemesEnum result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.stun).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.stun;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.stuns).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.stuns;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.turn).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.turn;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.STUNSchemesEnum.turns).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.STUNSchemesEnum.turns;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.STUNSchemesEnum)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }

    public static partial class RTCIceComponentExtensions
    {
        /// <summary>
        /// Returns a boolean telling whether an enum with the given name exists in the enumeration
        /// </summary>
        /// <param name="name">The name to check if it's defined</param>
        /// <returns><c>true</c> if a member with the name exists in the enumeration, <c>false</c> otherwise</returns>
        public static bool IsDefined(in global::System.ReadOnlySpan<char> name) => IsDefined(name, allowMatchingMetadataAttribute: false);

        /// <summary>
        /// Returns a boolean telling whether an enum with the given name exists in the enumeration,
        /// or optionally if a member decorated with a <c>[Display]</c> attribute
        /// with the required name exists.
        /// Slower then the <see cref="IsDefined(string, bool)" /> overload, but doesn't allocate memory./>
        /// </summary>
        /// <param name="name">The name to check if it's defined</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value of metadata attributes,otherwise ignores them</param>
        /// <returns><c>true</c> if a member with the name exists in the enumeration, or a member is decorated
        /// with a <c>[Display]</c> attribute with the name, <c>false</c> otherwise</returns>
        public static bool IsDefined(in global::System.ReadOnlySpan<char> name, bool allowMatchingMetadataAttribute)
        {
            return name switch
            {
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceComponent.rtp).AsSpan(), global::System.StringComparison.Ordinal) => true,
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceComponent.rtcp).AsSpan(), global::System.StringComparison.Ordinal) => true,
                _ => false,
            };
        }

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceComponent" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceComponent" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceComponent Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name)
                => TryParse(name, out var value, false, false) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceComponent" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceComponent" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceComponent Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            bool ignoreCase)
                => TryParse(name, out var value, ignoreCase, false) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceComponent" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value included in metadata attributes such as
        /// <c>[Display]</c> attribute when parsing, otherwise only considers the member names.</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceComponent" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceComponent Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => TryParse(name, out var value, ignoreCase, allowMatchingMetadataAttribute) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceComponent" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="value">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceComponent" /> whose
        /// value is represented by <paramref name="value"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceComponent" />. This parameter is passed uninitialized.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceComponent value)
            => TryParse(name, out value, false, false);

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceComponent" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="value">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceComponent" /> whose
        /// value is represented by <paramref name="value"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceComponent" />. This parameter is passed uninitialized.</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceComponent value,
            bool ignoreCase)
            => TryParse(name, out value, ignoreCase, false);

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceComponent" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="result">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceComponent" /> whose
        /// value is represented by <paramref name="result"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceComponent" />. This parameter is passed uninitialized.</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value included in metadata attributes such as
        /// <c>[Display]</c> attribute when parsing, otherwise only considers the member names.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceComponent result,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => ignoreCase
                    ? TryParseIgnoreCase(in name, out result, allowMatchingMetadataAttribute)
                    : TryParseWithCase(in name, out result, allowMatchingMetadataAttribute);

        private static bool TryParseIgnoreCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceComponent result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceComponent.rtp).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceComponent.rtp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceComponent.rtcp).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceComponent.rtcp;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.RTCIceComponent)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseWithCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceComponent result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceComponent.rtp).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceComponent.rtp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceComponent.rtcp).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceComponent.rtcp;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.RTCIceComponent)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }

    public static partial class RTCIceProtocolExtensions
    {
        /// <summary>
        /// Returns a boolean telling whether an enum with the given name exists in the enumeration
        /// </summary>
        /// <param name="name">The name to check if it's defined</param>
        /// <returns><c>true</c> if a member with the name exists in the enumeration, <c>false</c> otherwise</returns>
        public static bool IsDefined(in global::System.ReadOnlySpan<char> name) => IsDefined(name, allowMatchingMetadataAttribute: false);

        /// <summary>
        /// Returns a boolean telling whether an enum with the given name exists in the enumeration,
        /// or optionally if a member decorated with a <c>[Display]</c> attribute
        /// with the required name exists.
        /// Slower then the <see cref="IsDefined(string, bool)" /> overload, but doesn't allocate memory./>
        /// </summary>
        /// <param name="name">The name to check if it's defined</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value of metadata attributes,otherwise ignores them</param>
        /// <returns><c>true</c> if a member with the name exists in the enumeration, or a member is decorated
        /// with a <c>[Display]</c> attribute with the name, <c>false</c> otherwise</returns>
        public static bool IsDefined(in global::System.ReadOnlySpan<char> name, bool allowMatchingMetadataAttribute)
        {
            return name switch
            {
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceProtocol.udp).AsSpan(), global::System.StringComparison.Ordinal) => true,
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceProtocol.tcp).AsSpan(), global::System.StringComparison.Ordinal) => true,
                _ => false,
            };
        }

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceProtocol Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name)
                => TryParse(name, out var value, false, false) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceProtocol Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            bool ignoreCase)
                => TryParse(name, out var value, ignoreCase, false) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value included in metadata attributes such as
        /// <c>[Display]</c> attribute when parsing, otherwise only considers the member names.</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceProtocol Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => TryParse(name, out var value, ignoreCase, allowMatchingMetadataAttribute) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="value">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> whose
        /// value is represented by <paramref name="value"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceProtocol" />. This parameter is passed uninitialized.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceProtocol value)
            => TryParse(name, out value, false, false);

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="value">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> whose
        /// value is represented by <paramref name="value"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceProtocol" />. This parameter is passed uninitialized.</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceProtocol value,
            bool ignoreCase)
            => TryParse(name, out value, ignoreCase, false);

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="result">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceProtocol" /> whose
        /// value is represented by <paramref name="result"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceProtocol" />. This parameter is passed uninitialized.</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value included in metadata attributes such as
        /// <c>[Display]</c> attribute when parsing, otherwise only considers the member names.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceProtocol result,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => ignoreCase
                    ? TryParseIgnoreCase(in name, out result, allowMatchingMetadataAttribute)
                    : TryParseWithCase(in name, out result, allowMatchingMetadataAttribute);

        private static bool TryParseIgnoreCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceProtocol result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceProtocol.udp).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceProtocol.udp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceProtocol.tcp).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceProtocol.tcp;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.RTCIceProtocol)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseWithCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceProtocol result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceProtocol.udp).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceProtocol.udp;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceProtocol.tcp).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceProtocol.tcp;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.RTCIceProtocol)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }
    public static partial class RTCIceCandidateTypeExtensions
    {
        /// <summary>
        /// Returns a boolean telling whether an enum with the given name exists in the enumeration
        /// </summary>
        /// <param name="name">The name to check if it's defined</param>
        /// <returns><c>true</c> if a member with the name exists in the enumeration, <c>false</c> otherwise</returns>
        public static bool IsDefined(in global::System.ReadOnlySpan<char> name) => IsDefined(name, allowMatchingMetadataAttribute: false);

        /// <summary>
        /// Returns a boolean telling whether an enum with the given name exists in the enumeration,
        /// or optionally if a member decorated with a <c>[Display]</c> attribute
        /// with the required name exists.
        /// Slower then the <see cref="IsDefined(string, bool)" /> overload, but doesn't allocate memory./>
        /// </summary>
        /// <param name="name">The name to check if it's defined</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value of metadata attributes,otherwise ignores them</param>
        /// <returns><c>true</c> if a member with the name exists in the enumeration, or a member is decorated
        /// with a <c>[Display]</c> attribute with the name, <c>false</c> otherwise</returns>
        public static bool IsDefined(in global::System.ReadOnlySpan<char> name, bool allowMatchingMetadataAttribute)
        {
            return name switch
            {
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.host).AsSpan(), global::System.StringComparison.Ordinal) => true,
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.prflx).AsSpan(), global::System.StringComparison.Ordinal) => true,
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.srflx).AsSpan(), global::System.StringComparison.Ordinal) => true,
                global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.relay).AsSpan(), global::System.StringComparison.Ordinal) => true,
                _ => false,
            };
        }

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceCandidateType Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name)
                => TryParse(name, out var value, false, false) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceCandidateType Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            bool ignoreCase)
                => TryParse(name, out var value, ignoreCase, false) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the string representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> to the equivalent instance.
        /// </summary>
        /// <param name="name">The case-sensitive string representation of the enumeration name or underlying value to convert</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value included in metadata attributes such as
        /// <c>[Display]</c> attribute when parsing, otherwise only considers the member names.</param>
        /// <returns>An object of type <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> whose
        /// value is represented by <paramref name="name"/></returns>
        public static global::SIPSorcery.Net.RTCIceCandidateType Parse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => TryParse(name, out var value, ignoreCase, allowMatchingMetadataAttribute) ? value : ThrowValueNotFound(name.ToString());

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="value">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> whose
        /// value is represented by <paramref name="value"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceCandidateType" />. This parameter is passed uninitialized.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceCandidateType value)
            => TryParse(name, out value, false, false);

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="value">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> whose
        /// value is represented by <paramref name="value"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceCandidateType" />. This parameter is passed uninitialized.</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceCandidateType value,
            bool ignoreCase)
            => TryParse(name, out value, ignoreCase, false);

        /// <summary>
        /// Converts the span representation of the name or numeric value of
        /// an <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> to the equivalent instance.
        /// The return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="name">The span representation of the enumeration name or underlying value to convert</param>
        /// <param name="result">When this method returns, contains an object of type
        /// <see cref="global::SIPSorcery.Net.RTCIceCandidateType" /> whose
        /// value is represented by <paramref name="result"/> if the parse operation succeeds.
        /// If the parse operation fails, contains the default value of the underlying type
        /// of <see cref="global::SIPSorcery.Net.RTCIceCandidateType" />. This parameter is passed uninitialized.</param>
        /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
        /// <param name="allowMatchingMetadataAttribute">If <c>true</c>, considers the value included in metadata attributes such as
        /// <c>[Display]</c> attribute when parsing, otherwise only considers the member names.</param>
        /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceCandidateType result,
            bool ignoreCase,
            bool allowMatchingMetadataAttribute)
                => ignoreCase
                    ? TryParseIgnoreCase(in name, out result, allowMatchingMetadataAttribute)
                    : TryParseWithCase(in name, out result, allowMatchingMetadataAttribute);

        private static bool TryParseIgnoreCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceCandidateType result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.host).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.host;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.prflx).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.prflx;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.srflx).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.srflx;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.relay).AsSpan(), global::System.StringComparison.OrdinalIgnoreCase):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.relay;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.RTCIceCandidateType)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryParseWithCase(
#if NETCOREAPP3_0_OR_GREATER
            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
#endif
            in global::System.ReadOnlySpan<char> name,
            out global::SIPSorcery.Net.RTCIceCandidateType result,
            bool allowMatchingMetadataAttribute)
        {
            switch (name)
            {
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.host).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.host;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.prflx).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.prflx;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.srflx).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.srflx;
                    return true;
                case global::System.ReadOnlySpan<char> current when global::System.MemoryExtensions.Equals(current, nameof(global::SIPSorcery.Net.RTCIceCandidateType.relay).AsSpan(), global::System.StringComparison.Ordinal):
                    result = global::SIPSorcery.Net.RTCIceCandidateType.relay;
                    return true;
                case global::System.ReadOnlySpan<char> current when int.TryParse(name.ToString(), out var numericResult):
                    result = (global::SIPSorcery.Net.RTCIceCandidateType)numericResult;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }
}
#endif
