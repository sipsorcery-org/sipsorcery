//-----------------------------------------------------------------------------
// Filename: RTSPResponse.cs
//
// Description: RTSP response.
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

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RTSPResponseParserError
    {
        None = 0,
    }

    /// <summary>
    /// RFC2326 7.1:
    /// Status-Line =   RTSP-Version SP Status-Code SP Reason-Phrase CRLF
    /// </summary>
    public class RTSPResponse
    {
        private static readonly ILogger logger = LogFactory.CreateLogger<RTSPResponse>();

        private static string m_CRLF = RTSPConstants.CRLF;
        private static string m_rtspVersion = RTSPConstants.RTSP_VERSION_STRING;
        private static int m_rtspMajorVersion = RTSPConstants.RTSP_MAJOR_VERSION;
        private static int m_rtspMinorVersion = RTSPConstants.RTSP_MINOR_VERSION;

        public bool Valid = true;
        public RTSPHeaderError ValidationError = RTSPHeaderError.None;

        public string RTSPVersion = m_rtspVersion;
        public int RTSPMajorVersion = m_rtspMajorVersion;
        public int RTSPMinorVersion = m_rtspMinorVersion;
        public RTSPResponseStatusCodesEnum Status;
        public int StatusCode;
        public string ReasonPhrase;
        public string Body;
        public RTSPHeader Header;

        public DateTime ReceivedAt = DateTime.MinValue;
        public IPEndPoint ReceivedFrom;

        private RTSPResponse()
        { }

        public RTSPResponse(RTSPResponseStatusCodesEnum responseType, string reasonPhrase)
        {
            StatusCode = (int)responseType;
            Status = responseType;
            ReasonPhrase = reasonPhrase;
            ReasonPhrase = responseType.ToString();
        }

        public static RTSPResponse ParseRTSPResponse(RTSPMessage rtspMessage)
        {
            RTSPResponseParserError dontCare = RTSPResponseParserError.None;
            return ParseRTSPResponse(rtspMessage, out dontCare);
        }

        public static RTSPResponse ParseRTSPResponse(RTSPMessage rtspMessage, out RTSPResponseParserError responseParserError)
        {
            responseParserError = RTSPResponseParserError.None;

            try
            {
                RTSPResponse rtspResponse = new RTSPResponse();

                var statusLine = rtspMessage.FirstLine.AsSpan();
                var firstSpacePosn = statusLine.IndexOf(' ');

                rtspResponse.RTSPVersion = statusLine.Slice(0, firstSpacePosn).Trim().ToString();
                statusLine = statusLine.Slice(firstSpacePosn).Trim();
                rtspResponse.StatusCode = Convert.ToInt32(statusLine.Slice(0, 3).ToString());
                rtspResponse.Status = RTSPResponseStatusCodes.GetStatusTypeForCode(rtspResponse.StatusCode);
                rtspResponse.ReasonPhrase = statusLine.Slice(3).Trim().ToString();

                rtspResponse.Header = RTSPHeader.ParseRTSPHeaders(rtspMessage.RTSPHeaders);
                rtspResponse.Body = rtspMessage.Body;

                return rtspResponse;
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception parsing RTSP response. {ErrorMessage}", excp.Message);
                throw new ApplicationException($"There was an exception parsing an RTSP response. {excp.Message}");
            }
        }

        public override string ToString()
        {
            try
            {
                string reasonPhrase = (!ReasonPhrase.IsNullOrBlank()) ? $" {ReasonPhrase}" : null;

                var message =
                    $"{RTSPVersion}/{RTSPMajorVersion}.{RTSPMinorVersion} {StatusCode}{reasonPhrase}{m_CRLF}{Header}";

                if (Body != null)
                {
                    message += m_CRLF + Body;
                }
                else
                {
                    message += m_CRLF;
                }

                return message;
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception RTSPResponse ToString. {ErrorMessage}", excp.Message);
                throw;
            }
        }
    }
}
