//-----------------------------------------------------------------------------
// Filename: RTSPRequest.cs
//
// Description: RTSP request.
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
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RTSPRequestParserError
    {
        None = 0,
    }

    /// <summary>
    /// RFC2326 6.1:
    /// Request-Line = Method SP Request-URI SP RTSP-Version CRLF
    /// </summary>
    public class RTSPRequest
    {
        private static readonly ILogger logger = LogFactory.CreateLogger<RTSPRequest>();

        private static string m_CRLF = RTSPConstants.CRLF;
        private static string m_rtspFullVersion = RTSPConstants.RTSP_FULLVERSION_STRING;
        private static string m_rtspVersion = RTSPConstants.RTSP_VERSION_STRING;
        private static int m_rtspMajorVersion = RTSPConstants.RTSP_MAJOR_VERSION;
        private static int m_rtspMinorVersion = RTSPConstants.RTSP_MINOR_VERSION;

        public bool Valid = true;
        public RTSPHeaderError ValidationError = RTSPHeaderError.None;

        public string RTSPVersion = m_rtspVersion;
        public int RTSPMajorVersion = m_rtspMajorVersion;
        public int RTSPMinorVersion = m_rtspMinorVersion;
        public RTSPMethodsEnum Method;
        public string UnknownMethod = null;

        public RTSPURL URL;
        public RTSPHeader Header;
        public string Body;

        public DateTime ReceivedAt = DateTime.MinValue;
        public IPEndPoint ReceivedFrom;

        private RTSPRequest()
        { }

        public RTSPRequest(RTSPMethodsEnum method, string url)
        {
            RTSPHeaderParserError urlParserError = RTSPHeaderParserError.None;

            try
            {
                Method = method;
                RTSPVersion = m_rtspFullVersion;

                if (!string.IsNullOrEmpty(url))
                {
                    URL = RTSPURL.ParseRTSPURL(url, out urlParserError);
                    if (urlParserError != RTSPHeaderParserError.None)
                    {
                        throw new ApplicationException($"Error parsing RTSP URL, {urlParserError.ToString()}.");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogRtspExceptionMethod(nameof(RTSPRequest), excp);
            }
        }

        public RTSPRequest(RTSPMethodsEnum method, RTSPURL url)
        {
            Method = method;
            URL = url;
            RTSPVersion = m_rtspFullVersion;
        }

        public static RTSPRequest ParseRTSPRequest(RTSPMessage rtspMessage)
        {
            RTSPRequestParserError dontCare = RTSPRequestParserError.None;
            return ParseRTSPRequest(rtspMessage, out dontCare);
        }

        public static RTSPRequest ParseRTSPRequest(RTSPMessage rtspMessage, out RTSPRequestParserError requestParserError)
        {
            requestParserError = RTSPRequestParserError.None;
            string urlStr = null;

            try
            {
                RTSPRequest rtspRequest = new RTSPRequest();

                var statusLine = rtspMessage.FirstLine.AsSpan();
                var firstSpacePosn = statusLine.IndexOf(' ');
                var method = statusLine.Slice(0, firstSpacePosn).Trim().ToString();
                rtspRequest.Method = RTSPMethods.GetMethod(method);
                if (rtspRequest.Method == RTSPMethodsEnum.UNKNOWN)
                {
                    rtspRequest.UnknownMethod = method;
                    logger.LogRtspUnknownMethodWarning(rtspRequest.Method);
                }

                statusLine = statusLine.Slice(firstSpacePosn).Trim();
                var secondSpacePosn = statusLine.IndexOf(' ');

                if (secondSpacePosn != -1)
                {
                    urlStr = statusLine.Slice(0, secondSpacePosn).ToString();

                    rtspRequest.URL = RTSPURL.ParseRTSPURL(urlStr);
                    rtspRequest.RTSPVersion = statusLine.Slice(secondSpacePosn).Trim().ToString();
                    rtspRequest.Header = (rtspMessage.RTSPHeaders != null) ? RTSPHeader.ParseRTSPHeaders(rtspMessage.RTSPHeaders) : new RTSPHeader(0, null);
                    rtspRequest.Body = rtspMessage.Body;

                    return rtspRequest;
                }
                else
                {
                    throw new ApplicationException("URI was missing on RTSP request.");
                }
            }
            catch (Exception excp)
            {
                logger.LogRtspRequestParseError(urlStr, excp.Message, excp);
                throw new ApplicationException($"There was an exception parsing an RTSP request. {excp.Message}");
            }
        }

        public override string ToString()
        {
            try
            {
                string methodStr = (Method != RTSPMethodsEnum.UNKNOWN) ? Method.ToString() : UnknownMethod;
                string message = $"{methodStr + (URL == null ? "" : ($" {URL?.ToString()}"))} {RTSPVersion}{m_CRLF}";
                if (Header != null)
                {
                    message += Header.ToString();
                }
                if (Body != null)
                {
                    message += m_CRLF + Body;
                }

                return message;
            }
            catch (Exception excp)
            {
                logger.LogRtspRequestToStringError(excp.Message, excp);
                throw;
            }
        }
    }
}
