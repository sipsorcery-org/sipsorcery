using System;

[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.STUNMessageTypesEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.STUNAttributeTypesEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.SctpErrorCauseCode>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.SDPMediaTypesEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.SDPSecurityDescription.SessionParameter.SrtpSessionParams>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.AlertLevelsEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.AlertTypesEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.SctpChunkType>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.DataChannelTypes>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.DataChannelPayloadProtocols>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.STUNSchemesEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.IceRolesEnum>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.SDPSecurityDescription.SessionParameter.FecTypes>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::SIPSorcery.Net.SDPSecurityDescription.CryptoSuites>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::Org.BouncyCastle.Bcpg.HashAlgorithmTag>()]
//[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::System.Net.Sockets.AddressFamily>()]
[assembly: global::NetEscapades.EnumGenerators.EnumExtensions<global::System.Net.Sockets.SocketError>()]

#if NETFRAMEWORK || NETSTANDARD
namespace SIPSorcery.Net
{
    static partial class IceRolesEnumExtensions
    {
        public static bool TryParse(
                ReadOnlySpan<char> name,
                out IceRolesEnum value,
                bool ignoreCase)
            => System.Enum.TryParse<IceRolesEnum>(name.ToString(), ignoreCase, out value);
    }
}
#endif
