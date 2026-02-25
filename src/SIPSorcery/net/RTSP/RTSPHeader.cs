//-----------------------------------------------------------------------------
// Filename: RTSPHeader.cs
//
// Description: RTSP header.
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Nov 2007	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Polyfills;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RTSPHeaderParserError
    {
        None = 0,
        MandatoryColonMissing = 1,
        CSeqMethodMissing = 2,
        CSeqNotValidInteger = 3,
        CSeqEmpty = 4,
    }

    public enum RTSPHeaderError
    {
        None = 0,
    }

    /// <summary>
    /// Represents the Transport header used in RTSP requests and responses.
    /// </summary>
    public class RTSPTransportHeader
    {
        public const string DESTINATION_FIELD_NAME = "destination";
        public const string SOURCE_FIELD_NAME = "source";
        public const string MULTICAST_RTP_PORT_FIELD_NAME = "port";
        public const string CLIENT_RTP_PORT_FIELD_NAME = "client_port";
        public const string SERVER_RTP_PORT_FIELD_NAME = "server_port";
        public const string MODE_FIELD_NAME = "mode";

        private const string DEFAULT_TRANSPORT_SPECIFIER = "RTP/AVP/UDP";
        private const string DEFAULT_BROADCAST_TYPE = "unicast";

        private static readonly ILogger logger = LogFactory.CreateLogger<RTSPTransportHeader>();

        public string RawHeader;

        public string TransportSpecifier;   // RTP/AVP{/[TCP/UDP]}, default is UDP.
        public string BroadcastType;        // Unicast or multicast.
        public string Destination;
        public string Source;
        public string MulticastRTPPortRange;// e.g. port=3456-3457.
        public string ClientRTPPortRange;   // e.g. client_port=3456-3457.
        public string ServerRTPPortRange;   // e.g. server_port=3456-3457.
        public string Mode;

        public RTSPTransportHeader()
        {
            TransportSpecifier = DEFAULT_TRANSPORT_SPECIFIER;
            BroadcastType = DEFAULT_BROADCAST_TYPE;
        }

        public static RTSPTransportHeader Parse(string header)
        {
            var transportHeader = new RTSPTransportHeader();

            if (header.NotNullOrBlank())
            {
                transportHeader.RawHeader = header;

                var fields = header.AsSpan();
                var fieldIndex = 0;
                Span<Range> fieldKeyValueRange = stackalloc Range[3];
                foreach (var fieldRange in fields.Split(';'))
                {
                    var field = fields[fieldRange];
                    if (fieldIndex == 0)
                    {
                        transportHeader.TransportSpecifier = field.ToString();
                        fieldIndex++;
                        continue;
                    }

                    if (fieldIndex == 1)
                    {
                        transportHeader.BroadcastType = field.ToString();
                        fieldIndex++;
                        continue;
                    }

                    if (field.Split(fieldKeyValueRange, '=') < 2)
                    {
                        continue;
                    }

                    var fieldName = field[fieldKeyValueRange[0]];
                    var fieldValue = field[fieldKeyValueRange[1]];

                    switch (fieldName)
                    {
                        case var fn when (CLIENT_RTP_PORT_FIELD_NAME.Equals(fn, StringComparison.OrdinalIgnoreCase)):
                            transportHeader.ClientRTPPortRange = fieldValue.Trim().ToString();
                            break;
                        case var fn when (DESTINATION_FIELD_NAME.Equals(fn, StringComparison.OrdinalIgnoreCase)):
                            transportHeader.Destination = fieldValue.Trim().ToString();
                            break;
                        case var fn when (SERVER_RTP_PORT_FIELD_NAME.Equals(fn, StringComparison.OrdinalIgnoreCase)):
                            transportHeader.ServerRTPPortRange = fieldValue.Trim().ToString();
                            break;
                        case var fn when (SOURCE_FIELD_NAME.Equals(fn, StringComparison.OrdinalIgnoreCase)):
                            transportHeader.Source = fieldValue.Trim().ToString();
                            break;
                        case var fn when (MODE_FIELD_NAME.Equals(fn, StringComparison.OrdinalIgnoreCase)):
                            transportHeader.Mode = fieldValue.Trim().ToString();
                            break;
                        default:
                            logger.LogRtspUnrecognisedHeaderParameter(field);
                            break;
                    }

                    fieldIndex++;
                }
            }

            return transportHeader;
        }

        /// <summary>
        /// Attempts to determine the client RTP port based on the transport header attributes.
        /// </summary>
        /// <returns>The client port that RTP packets should be sent to. If the port cannot be determined then 0.</returns>
        public int GetClientRTPPort()
        {
            if (ClientRTPPortRange.NotNullOrBlank())
            {
                if (TryParsePortRange(ClientRTPPortRange.AsSpan(), true, out var clientRTPPort))
                {
                    return clientRTPPort;
                }
            }

            return 0;
        }

        /// <summary>
        /// Attempts to determine the client RTCP port based on the transport header attributes.
        /// </summary>
        /// <returns>The client port that RTCP packets should be sent to. If the port cannot be determined then 0.</returns>
        public int GetClientRtcpPort()
        {
            if (ClientRTPPortRange.NotNullOrBlank())
            {
                if (TryParsePortRange(ClientRTPPortRange.AsSpan(), false, out var clientRTCPPort))
                {
                    return clientRTCPPort;
                }
            }

            return 0;
        }


        /// <summary>
        /// Attempts to determine the server RTP port based on the transport header attributes.
        /// </summary>
        /// <returns>The server port that RTP packets should be sent to. If the port cannot be determined then 0.</returns>
        public int GetServerRTPPort()
        {
            if (ServerRTPPortRange.NotNullOrBlank())
            {
                if (TryParsePortRange(ServerRTPPortRange.AsSpan(), true, out var serverRTPPort))
                {
                    return serverRTPPort;
                }
            }

            return 0;
        }

        /// <summary>
        /// Attempts to determine the server Rtcp port based on the transport header attributes.
        /// </summary>
        /// <returns>The server port that RTCP packets should be sent to. If the port cannot be determined then 0.</returns>
        public int GetServerRtcpPort()
        {
            if (ServerRTPPortRange.NotNullOrBlank())
            {
                if (TryParsePortRange(ServerRTPPortRange.AsSpan(), false, out var serverRtcpPort))
                {
                    return serverRtcpPort;
                }
            }

            return 0;
        }

        private static bool TryParsePortRange(ReadOnlySpan<char> portRange, bool useFirstPort, out int port)
        {
            port = 0;
            portRange = portRange.Trim();
            Span<Range> portRangeFields = stackalloc Range[3];
            var portFieldCount = portRange.Split(portRangeFields, '-');
            var portIndex = useFirstPort ? 0 : 1;

            return portFieldCount > portIndex && int.TryParse(portRange[portRangeFields[portIndex]], out port);
        }

        public override string ToString()
        {
            var transportHeader = new StringBuilder();
            transportHeader.Append(TransportSpecifier).Append(';').Append(BroadcastType);

            if (Destination.NotNullOrBlank())
            {
                transportHeader.Append(';').Append(DESTINATION_FIELD_NAME).Append('=').Append(Destination);
            }

            if (Source.NotNullOrBlank())
            {
                transportHeader.Append(';').Append(SOURCE_FIELD_NAME).Append('=').Append(Source);
            }

            if (ClientRTPPortRange.NotNullOrBlank())
            {
                transportHeader.Append(';').Append(CLIENT_RTP_PORT_FIELD_NAME).Append('=').Append(ClientRTPPortRange);
            }

            if (ServerRTPPortRange.NotNullOrBlank())
            {
                transportHeader.Append(';').Append(SERVER_RTP_PORT_FIELD_NAME).Append('=').Append(ServerRTPPortRange);
            }

            if (Mode.NotNullOrBlank())
            {
                transportHeader.Append(';').Append(MODE_FIELD_NAME).Append('=').Append(Mode);
            }

            return transportHeader.ToString();
        }
    }


    public class RTSPHeader
    {
        private static string m_CRLF = RTSPConstants.CRLF;

        private static readonly ILogger logger = LogFactory.CreateLogger<RTSPHeader>();

        private static char[] delimiterChars = new char[] { ':' };

        public string Accept;
        public string ContentType;
        public int ContentLength;
        public int CSeq = -1;
        public string Session;
        public RTSPTransportHeader Transport;

        public List<string> UnknownHeaders = new List<string>();    // Holds any unrecognised headers.

        public string RawCSeq;

        public RTSPHeaderParserError CSeqParserError = RTSPHeaderParserError.None;

        private RTSPHeader()
        { }

        public RTSPHeader(int cseq, string session)
        {
            CSeq = cseq;
            Session = session;
        }

        public void AddHeader(string headerName, string value)
        {
            UnknownHeaders.Add($"{headerName}: {value}");
        }

        public static string[] SplitHeaders(string message)
        {
            static string NormalizeFoldedHeaderLines(string headerBlock)
            {
                var normalised = default(StringBuilder);
                var segmentStart = 0;
                var position = 0;

                while (position < headerBlock.Length)
                {
                    if (position + 2 < headerBlock.Length &&
                        headerBlock[position] == '\r' &&
                        headerBlock[position + 1] == '\n' &&
                        char.IsWhiteSpace(headerBlock[position + 2]))
                    {
                        normalised ??= new StringBuilder(headerBlock.Length);
                        normalised.Append(headerBlock, segmentStart, position - segmentStart);
                        normalised.Append(' ');

                        position += 2;
                        while (position < headerBlock.Length && char.IsWhiteSpace(headerBlock[position]))
                        {
                            position++;
                        }

                        segmentStart = position;
                        continue;
                    }

                    if (position + 1 < headerBlock.Length &&
                        headerBlock[position] == '\r' &&
                        headerBlock[position + 1] == ' ')
                    {
                        normalised ??= new StringBuilder(headerBlock.Length);
                        normalised.Append(headerBlock, segmentStart, position - segmentStart);
                        normalised.Append(m_CRLF);

                        position += 2;
                        segmentStart = position;
                        continue;
                    }

                    position++;
                }

                if (normalised is null)
                {
                    return headerBlock;
                }

                normalised.Append(headerBlock, segmentStart, headerBlock.Length - segmentStart);
                return normalised.ToString();
            }

            // SIP headers can be extended across lines if the first character of the next line is at least on whitespace character.
            // Some user agents couldn't get the \r\n bit right; normalise those at the same time.
            message = NormalizeFoldedHeaderLines(message);

            var headers = new List<string>();
            var messageSpan = message.AsSpan();

            foreach (var headerRange in messageSpan.Split(m_CRLF.AsSpan()))
            {
                headers.Add(messageSpan[headerRange].ToString());
            }

            return headers.ToArray();
        }

        public static RTSPHeader ParseRTSPHeaders(string[] headersCollection)
        {
            try
            {
                RTSPHeader rtspHeader = new RTSPHeader();

                string lastHeader = null;

                for (int lineIndex = 0; lineIndex < headersCollection.Length; lineIndex++)
                {
                    string headerLine = headersCollection[lineIndex];

                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        // No point processing blank headers.
                        continue;
                    }

                    string headerName = null;
                    string headerValue = null;

                    // If the first character of a line is whitespace it's a continuation of the previous line.
                    if (headerLine.StartsWith(" ", StringComparison.Ordinal))
                    {
                        headerName = lastHeader;
                        headerValue = headerLine.Trim();
                    }
                    else
                    {
                        var headerLineSpan = headerLine.AsSpan().Trim();
                        var delimiterIndex = headerLineSpan.IndexOf(':');

                        if (delimiterIndex == -1)
                        {
                            logger.LogRtspInvalidHeaderIgnoring(headerLine);

                            try
                            {
                                string errorHeaders = String.Join(m_CRLF, headersCollection);
                                logger.LogRtspFullInvalidHeaders(errorHeaders);
                            }
                            catch { }

                            continue;
                        }

                        headerName = headerLineSpan.Slice(0, delimiterIndex).Trim().ToString();
                        headerValue = headerLineSpan.Slice(delimiterIndex + 1).Trim().ToString();
                    }

                    try
                    {
                        #region Accept
                        if (string.Equals(headerName, RTSPHeaders.RTSP_HEADER_ACCEPT, StringComparison.OrdinalIgnoreCase))
                        {
                            rtspHeader.Accept = headerValue;
                        }
                        #endregion
                        #region ContentType
                        if (string.Equals(headerName, RTSPHeaders.RTSP_HEADER_CONTENTTYPE, StringComparison.OrdinalIgnoreCase))
                        {
                            rtspHeader.ContentType = headerValue;
                        }
                        #endregion
                        #region ContentLength
                        if (string.Equals(headerName, RTSPHeaders.RTSP_HEADER_CONTENTLENGTH, StringComparison.OrdinalIgnoreCase))
                        {
                            rtspHeader.RawCSeq = headerValue;

                            if (string.IsNullOrWhiteSpace(headerValue))
                            {
                                logger.LogRtspContentLengthEmptyWarning(RTSPHeaders.RTSP_HEADER_CONTENTLENGTH);
                            }
                            else if (!Int32.TryParse(headerValue.Trim(), out rtspHeader.ContentLength))
                            {
                                logger.LogRtspContentLengthNotIntWarning(RTSPHeaders.RTSP_HEADER_CONTENTLENGTH, headerValue);
                            }
                        }
                        #endregion
                        #region CSeq
                        if (string.Equals(headerName, RTSPHeaders.RTSP_HEADER_CSEQ, StringComparison.OrdinalIgnoreCase))
                        {
                            rtspHeader.RawCSeq = headerValue;

                            if (string.IsNullOrWhiteSpace(headerValue))
                            {
                                rtspHeader.CSeqParserError = RTSPHeaderParserError.CSeqEmpty;
                                logger.LogRtspCseqEmptyWarning(RTSPHeaders.RTSP_HEADER_CSEQ);
                            }
                            else if (!Int32.TryParse(headerValue.Trim(), out rtspHeader.CSeq))
                            {
                                rtspHeader.CSeqParserError = RTSPHeaderParserError.CSeqNotValidInteger;
                                logger.LogRtspCseqNotIntWarning(RTSPHeaders.RTSP_HEADER_CSEQ, headerValue);
                            }
                        }
                        #endregion
                        #region Session
                        if (string.Equals(headerName, RTSPHeaders.RTSP_HEADER_SESSION, StringComparison.OrdinalIgnoreCase))
                        {
                            rtspHeader.Session = headerValue;
                        }
                        #endregion
                        #region Transport
                        if (string.Equals(headerName, RTSPHeaders.RTSP_HEADER_TRANSPORT, StringComparison.OrdinalIgnoreCase))
                        {
                            rtspHeader.Transport = RTSPTransportHeader.Parse(headerValue);
                        }
                        #endregion
                        else
                        {
                            rtspHeader.UnknownHeaders.Add(headerLine);
                        }

                        lastHeader = headerName;
                    }
                    catch (Exception parseExcp)
                    {
                        logger.LogRtspHeaderParseError(headerLine, parseExcp.Message, parseExcp);
                        throw;
                    }
                }

                //sipHeader.Valid = sipHeader.Validate(out sipHeader.ValidationError);

                return rtspHeader;
            }
            catch (ApplicationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogRtspParseHeadersError(excp.Message, excp);
                throw;
            }
        }


        public override string ToString()
        {
            try
            {
                var headersBuilder = new StringBuilder();

                void AppendHeader<T>(string headerName, T headerValue) =>
                    headersBuilder.Append(headerName).Append(": ").Append(headerValue).Append(m_CRLF);

                if (CSeq > 0)
                {
                    AppendHeader(RTSPHeaders.RTSP_HEADER_CSEQ, CSeq);
                }

                if (Session != null)
                {
                    AppendHeader(RTSPHeaders.RTSP_HEADER_SESSION, Session);
                }

                if (Accept != null)
                {
                    AppendHeader(RTSPHeaders.RTSP_HEADER_ACCEPT, Accept);
                }

                if (ContentType != null)
                {
                    AppendHeader(RTSPHeaders.RTSP_HEADER_CONTENTTYPE, ContentType);
                }

                if (ContentLength != 0)
                {
                    AppendHeader(RTSPHeaders.RTSP_HEADER_CONTENTLENGTH, ContentLength);
                }

                if (Transport != null)
                {
                    AppendHeader(RTSPHeaders.RTSP_HEADER_TRANSPORT, Transport);
                }

                if (UnknownHeaders != null)
                {
                    foreach (var unknownHeader in UnknownHeaders)
                    {
                        headersBuilder.Append(unknownHeader).Append(m_CRLF);
                    }
                }

                return headersBuilder.ToString();
            }
            catch (Exception excp)
            {
                logger.LogRtspHeaderToStringError(excp.Message, excp);
                throw;
            }
        }
    }
}
