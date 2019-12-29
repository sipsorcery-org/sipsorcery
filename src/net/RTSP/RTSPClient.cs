//-----------------------------------------------------------------------------
// Filename: RTSPClient.cs
//
// Description: RTSP client functions.
//
// Author(s):
// Aaron Clauson
//
// History:
// 16 Nov 2007	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTSPClient
    {
        public const int RTSP_PORT = 554;
        private const int MAX_FRAMES_QUEUE_LENGTH = 1000;
        private const int RTP_KEEP_ALIVE_INTERVAL = 30;         // The interval at which to send RTP keep-alive packets to keep the RTSP server from closing the connection.
        private const int RTP_TIMEOUT_SECONDS = 15;             // If no RTP packets are received during this interval then assume the connection has failed.
        private const int BANDWIDTH_CALCULATION_SECONDS = 5;    // The interval at which to do bandwidth calculations.

        private static ILogger logger = Log.Logger;

        private string _url;
        private int _cseq = 1;
        private TcpClient _rtspConnection;
        private NetworkStream _rtspStream;
        private RTPSession _rtpSession;
        private int _rtpPayloadHeaderLength;
        private List<RTPFrame> _frames = new List<RTPFrame>();
        private uint _lastCompleteFrameTimestamp;
        private Action<string> _rtpTrackingAction;
        private DateTime _lastRTPReceivedAt;
        private int _lastFrameSize;
        private DateTime _lastBWCalcAt;
        private int _bytesSinceLastBWCalc;
        private int _framesSinceLastCalc;
        private double _lastBWCalc;
        private double _lastFrameRate;
        private ManualResetEvent _sendKeepAlivesMRE = new ManualResetEvent(false);

        public event Action<RTSPClient> OnSetupSuccess;
        public event Action<RTSPClient, RTPFrame> OnFrameReady;
        public event Action<RTSPClient> OnClosed;

        private bool _isClosed;
        public bool IsClosed
        {
            get { return _isClosed; }
        }

        public RTSPClient()
        { }

        public RTSPClient(Action<string> rtpTrackingAction)
        {
            _rtpTrackingAction = rtpTrackingAction;
        }

        public void SetRTPPayloadHeaderLength(int rtpPayloadHeaderLength)
        {
            _rtpPayloadHeaderLength = rtpPayloadHeaderLength;

            if (_rtspSession != null)
            {
                _rtspSession.RTPPayloadHeaderLength = rtpPayloadHeaderLength;
            }
        }

        public string GetStreamDescription(string url)
        {
            try
            {
                string hostname = Regex.Match(url, @"rtsp://(?<hostname>\S+?)/").Result("${hostname}");
                //IPEndPoint rtspEndPoint = DNSResolver.R(hostname, DNS_RESOLUTION_TIMEOUT);

                logger.LogDebug("RTSP Client Connecting to " + hostname + ".");
                TcpClient rtspSocket = new TcpClient(hostname, RTSP_PORT);
                NetworkStream rtspStream = rtspSocket.GetStream();

                string rtspSDP = null;
                RTSPRequest rtspRequest = new RTSPRequest(RTSPMethodsEnum.DESCRIBE, url);
                RTSPHeader rtspHeader = new RTSPHeader(1, null);
                rtspRequest.Header = rtspHeader;
                string rtspReqStr = rtspRequest.ToString();

                RTSPMessage rtspMessage = null;
                RTSPResponse rtspResponse = null;

                byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(rtspReqStr);
                rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

                byte[] buffer = new byte[2048];
                int bytesRead = rtspStream.Read(buffer, 0, 2048);

                if (bytesRead > 0)
                {
                    logger.LogDebug(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    byte[] msgBuffer = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, msgBuffer, 0, bytesRead);
                    rtspMessage = RTSPMessage.ParseRTSPMessage(msgBuffer, null, null);

                    if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                    {
                        rtspResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                        logger.LogDebug("RTSP Response received: " + rtspResponse.StatusCode + " " + rtspResponse.Status + " " + rtspResponse.ReasonPhrase + ".");
                    }

                    rtspSDP = rtspResponse.Body;
                }
                else
                {
                    logger.LogWarning("Socket closed prematurely in GetStreamDescription for " + url + ".");
                }

                rtspSocket.Close();

                return rtspSDP;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetStreamDescription. " + excp.Message);
                throw excp;
            }
        }

        public void Start(string url)
        {
            _url = url;

            Match urlMatch = Regex.Match(url, @"rtsp://(?<hostname>\S+?)/", RegexOptions.IgnoreCase);

            if (!urlMatch.Success)
            {
                throw new ApplicationException("The URL provided to the RTSP client was not recognised, " + url + ".");
            }
            else
            {
                string hostname = urlMatch.Result("${hostname}");
                int port = RTSP_PORT;

                if (hostname.Contains(':'))
                {
                    port = SIPSorcery.Sys.IPSocket.ParsePortFromSocket(hostname);
                    hostname = SIPSorcery.Sys.IPSocket.ParseHostFromSocket(hostname);
                }

                logger.LogDebug("RTSP client connecting to " + hostname + ", port " + port + ".");

                _rtspConnection = new TcpClient(hostname, port);
                _rtspStream = _rtspConnection.GetStream();

                _rtpSession = new RTPSession();
                _rtpSession.RTPPayloadHeaderLength = _rtpPayloadHeaderLength;
                _rtpSession.ReservePorts();
                _rtpSession.OnRTPQueueFull += RTPQueueFull;

                RTSPRequest rtspRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, url);
                RTSPHeader rtspHeader = new RTSPHeader(_cseq++, null);
                rtspHeader.Transport = new RTSPTransportHeader() { ClientRTPPortRange = _rtspSession.RTPPort + "-" + _rtspSession.ControlPort };
                rtspRequest.Header = rtspHeader;
                string rtspReqStr = rtspRequest.ToString();

                RTSPMessage rtspMessage = null;

                System.Diagnostics.Debug.WriteLine(rtspReqStr);

                byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(rtspReqStr);
                _rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

                byte[] buffer = new byte[2048];
                int bytesRead = _rtspStream.Read(buffer, 0, 2048);

                if (bytesRead > 0)
                {
                    System.Diagnostics.Debug.WriteLine(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

                    if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                    {
                        var setupResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);

                        if (setupResponse.Status == RTSPResponseStatusCodesEnum.OK)
                        {
                            _rtspSession.SessionID = setupResponse.Header.Session;
                            _rtspSession.RemoteEndPoint = new IPEndPoint((_rtspConnection.Client.RemoteEndPoint as IPEndPoint).Address, setupResponse.Header.Transport.GetServerRTPPort());
                            _rtspSession.Start();

                            logger.LogDebug("RTSP Response received to SETUP: " + setupResponse.Status + ", session ID " + _rtspSession.SessionID + ", server RTP endpoint " + _rtspSession.RemoteEndPoint + ".");

                            if (OnSetupSuccess != null)
                            {
                                OnSetupSuccess(this);
                            }
                        }
                        else
                        {
                            logger.LogWarning("RTSP Response received to SETUP: " + setupResponse.Status + ".");
                            throw new ApplicationException("An error response of " + setupResponse.Status + " was received for an RTSP setup request.");
                        }
                    }
                }
                else
                {
                    throw new ApplicationException("Zero bytes were read from the RTSP client socket in response to a SETUP request.");
                }
            }
        }

        private void RTPQueueFull()
        {
            try
            {
                logger.LogWarning("RTSPCient.RTPQueueFull purging frames list.");
                System.Diagnostics.Debug.WriteLine("RTSPCient.RTPQueueFull purging frames list.");

                lock (_frames)
                {
                    _frames.Clear();
                }

                _lastCompleteFrameTimestamp = 0;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPClient.RTPQueueFull. " + excp);
            }
        }

        /// <summary>
        /// Send a PLAY request to the RTSP server to commence the media stream.
        /// </summary>
        public void Play()
        {
            ThreadPool.QueueUserWorkItem(delegate
            { ProcessRTPPackets(); });
            ThreadPool.QueueUserWorkItem(delegate
            { SendKeepAlives(); });

            RTSPRequest playRequest = new RTSPRequest(RTSPMethodsEnum.PLAY, _url);
            RTSPHeader playHeader = new RTSPHeader(_cseq++, _rtspSession.SessionID);
            playRequest.Header = playHeader;

            System.Diagnostics.Debug.WriteLine(playRequest.ToString());

            var rtspRequestBuffer = Encoding.UTF8.GetBytes(playRequest.ToString());
            _rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

            var buffer = new byte[2048];
            var bytesRead = _rtspStream.Read(buffer, 0, 2048);

            if (bytesRead > 0)
            {
                System.Diagnostics.Debug.WriteLine(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                var rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

                if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                {
                    var playResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                    logger.LogDebug("RTSP Response received to PLAY: " + playResponse.StatusCode + " " + playResponse.Status + " " + playResponse.ReasonPhrase + ".");
                }
            }
            else
            {
                throw new ApplicationException("Zero bytes were read from the RTSP client socket in response to a PLAY request.");
            }
        }

        /// <summary>
        /// Sends the RTSP teardown request for an existing RTSP session.
        /// </summary>
        private void Teardown()
        {
            try
            {
                if (_rtspStream != null && _rtspConnection.Connected)
                {
                    logger.LogDebug("RTSP client sending teardown request for " + _url + ".");

                    RTSPRequest teardownRequest = new RTSPRequest(RTSPMethodsEnum.TEARDOWN, _url);
                    RTSPHeader teardownHeader = new RTSPHeader(_cseq++, _rtspSession.SessionID);
                    teardownRequest.Header = teardownHeader;

                    System.Diagnostics.Debug.WriteLine(teardownRequest.ToString());

                    var buffer = Encoding.UTF8.GetBytes(teardownRequest.ToString());
                    _rtspStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    logger.LogDebug("RTSP client did not send teardown request for " + _url + ", the socket was closed.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPClient.Teardown. " + excp);
            }
        }

        /// <summary>
        /// Closes the session and the RTSP connection to the server.
        /// </summary>
        public void Close()
        {
            try
            {
                if (!_isClosed)
                {
                    logger.LogDebug("RTSP client, closing connection for " + _url + ".");

                    _isClosed = true;
                    _sendKeepAlivesMRE.Set();

                    Teardown();

                    if (_rtspSession != null && !_rtspSession.IsClosed)
                    {
                        _rtspSession.Close();
                    }

                    if (_rtspStream != null)
                    {
                        try
                        {
                            _rtspStream.Close();
                        }
                        catch (Exception rtpStreamExcp)
                        {
                            logger.LogError("Exception RTSPClient.Close closing RTP stream. " + rtpStreamExcp);
                        }
                    }

                    if (OnClosed != null)
                    {
                        OnClosed(this);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPClient.Close. " + excp);
            }
        }

        private void ProcessRTPPackets()
        {
            try
            {
                Thread.CurrentThread.Name = "rtspclient-rtp";

                _lastRTPReceivedAt = DateTime.Now;
                _lastBWCalcAt = DateTime.Now;

                while (!_isClosed)
                {
                    while (_rtspSession.HasRTPPacket())
                    {
                        RTPPacket rtpPacket = _rtspSession.GetNextRTPPacket();

                        if (rtpPacket != null)
                        {
                            _lastRTPReceivedAt = DateTime.Now;
                            _bytesSinceLastBWCalc += RTPHeader.MIN_HEADER_LEN + rtpPacket.Payload.Length;

                            if (_rtpTrackingAction != null)
                            {
                                double bwCalcSeconds = DateTime.Now.Subtract(_lastBWCalcAt).TotalSeconds;
                                if (bwCalcSeconds > BANDWIDTH_CALCULATION_SECONDS)
                                {
                                    _lastBWCalc = _bytesSinceLastBWCalc * 8 / bwCalcSeconds;
                                    _lastFrameRate = _framesSinceLastCalc / bwCalcSeconds;
                                    _bytesSinceLastBWCalc = 0;
                                    _framesSinceLastCalc = 0;
                                    _lastBWCalcAt = DateTime.Now;
                                }

                                var abbrevURL = (_url.Length <= 50) ? _url : _url.Substring(0, 50);
                                string rtpTrackingText = String.Format("Url: {0}\r\nRcvd At: {1}\r\nSeq Num: {2}\r\nTS: {3}\r\nPayoad: {4}\r\nFrame Size: {5}\r\nBW: {6}\r\nFrame Rate: {7}", abbrevURL, DateTime.Now.ToString("HH:mm:ss:fff"), rtpPacket.Header.SequenceNumber, rtpPacket.Header.Timestamp, ((SDPMediaFormatsEnum)rtpPacket.Header.PayloadType).ToString(), _lastFrameSize + " bytes", _lastBWCalc.ToString("0.#") + "bps", _lastFrameRate.ToString("0.##") + "fps");
                                _rtpTrackingAction(rtpTrackingText);
                            }

                            if (rtpPacket.Header.Timestamp < _lastCompleteFrameTimestamp)
                            {
                                System.Diagnostics.Debug.WriteLine("Ignoring RTP packet with timestamp " + rtpPacket.Header.Timestamp + " as it's earlier than the last complete frame.");
                            }
                            else
                            {
                                while (_frames.Count > MAX_FRAMES_QUEUE_LENGTH)
                                {
                                    var oldestFrame = _frames.OrderBy(x => x.Timestamp).First();
                                    _frames.Remove(oldestFrame);
                                    System.Diagnostics.Debug.WriteLine("Receive queue full, dropping oldest frame with timestamp " + oldestFrame.Timestamp + ".");
                                }

                                var frame = _frames.Where(x => x.Timestamp == rtpPacket.Header.Timestamp).SingleOrDefault();

                                if (frame == null)
                                {
                                    frame = new RTPFrame() { Timestamp = rtpPacket.Header.Timestamp, HasMarker = rtpPacket.Header.MarkerBit == 1 };
                                    frame.AddRTPPacket(rtpPacket);
                                    _frames.Add(frame);
                                }
                                else
                                {
                                    frame.HasMarker = (rtpPacket.Header.MarkerBit == 1);
                                    frame.AddRTPPacket(rtpPacket);
                                }

                                if (frame.IsComplete())
                                {
                                    // The frame is ready for handing over to the UI.
                                    byte[] imageBytes = frame.GetFramePayload();

                                    _lastFrameSize = imageBytes.Length;
                                    _framesSinceLastCalc++;

                                    _lastCompleteFrameTimestamp = rtpPacket.Header.Timestamp;
                                    //System.Diagnostics.Debug.WriteLine("Frame ready " + frame.Timestamp + ", sequence numbers " + frame.StartSequenceNumber + " to " + frame.EndSequenceNumber + ",  payload length " + imageBytes.Length + ".");
                                    //logger.LogDebug("Frame ready " + frame.Timestamp + ", sequence numbers " + frame.StartSequenceNumber + " to " + frame.EndSequenceNumber + ",  payload length " + imageBytes.Length + ".");
                                    _frames.Remove(frame);

                                    // Also remove any earlier frames as we don't care about anything that's earlier than the current complete frame.
                                    foreach (var oldFrame in _frames.Where(x => x.Timestamp <= rtpPacket.Header.Timestamp).ToList())
                                    {
                                        System.Diagnostics.Debug.WriteLine("Discarding old frame for timestamp " + oldFrame.Timestamp + ".");
                                        logger.LogWarning("Discarding old frame for timestamp " + oldFrame.Timestamp + ".");
                                        _frames.Remove(oldFrame);
                                    }

                                    if (OnFrameReady != null)
                                    {
                                        try
                                        {
                                            //if (frame.FramePackets.Count == 1)
                                            //{
                                            //    // REMOVE.
                                            //    logger.LogWarning("Discarding frame as there should have been more than 1 RTP packets.");
                                            //}
                                            //else
                                            //{
                                            //System.Diagnostics.Debug.WriteLine("RTP frame ready for timestamp " + frame.Timestamp + ".");
                                            OnFrameReady(this, frame);
                                            //}
                                        }
                                        catch (Exception frameReadyExcp)
                                        {
                                            logger.LogError("Exception RTSPClient.ProcessRTPPackets OnFrameReady. " + frameReadyExcp);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (DateTime.Now.Subtract(_lastRTPReceivedAt).TotalSeconds > RTP_TIMEOUT_SECONDS)
                    {
                        logger.LogWarning("No RTP packets were received on RTSP session " + _rtspSession.SessionID + " for " + RTP_TIMEOUT_SECONDS + ". The session will now be closed.");
                        Close();
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPClient.ProcessRTPPackets. " + excp);
            }
        }

        /// <summary>
        /// Sends a keep-alive packet to keep the RTSP RTP connection from being shut.
        /// </summary>
        private void SendKeepAlives()
        {
            try
            {
                Thread.CurrentThread.Name = "rtspclient-keepalive";

                // Set the initial pause as half the keep-alive interval.
                Thread.Sleep(RTP_KEEP_ALIVE_INTERVAL * 500);

                while (!_isClosed)
                {
                    _rtspSession.SendRTPRaw(new byte[] { 0x00, 0x00, 0x00, 0x00 });

                    // Also send an OPTIONS request on the RTSP connection to prevent the remote server from timing it out.
                    RTSPRequest optionsRequest = new RTSPRequest(RTSPMethodsEnum.OPTIONS, _url);
                    RTSPHeader optionsHeader = new RTSPHeader(_cseq++, _rtspSession.SessionID);
                    optionsRequest.Header = optionsHeader;

                    System.Diagnostics.Debug.WriteLine(optionsRequest.ToString());

                    var rtspRequestBuffer = Encoding.UTF8.GetBytes(optionsRequest.ToString());
                    _rtspStream.Write(rtspRequestBuffer, 0, rtspRequestBuffer.Length);

                    var buffer = new byte[2048];
                    var bytesRead = _rtspStream.Read(buffer, 0, 2048);

                    if (bytesRead > 0)
                    {
                        System.Diagnostics.Debug.WriteLine(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                        var rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

                        if (rtspMessage.RTSPMessageType == RTSPMessageTypesEnum.Response)
                        {
                            var optionsResponse = RTSPResponse.ParseRTSPResponse(rtspMessage);
                            //logger.LogDebug("RTSP Response received for OPTIONS keep-alive request: " + optionsResponse.StatusCode + " " + optionsResponse.Status + " " + optionsResponse.ReasonPhrase + ".");
                        }
                    }
                    else
                    {
                        logger.LogWarning("Zero bytes were read from the RTSP client socket in response to an OPTIONS keep-alive request.");
                    }

                    _sendKeepAlivesMRE.Reset();
                    _sendKeepAlivesMRE.WaitOne(RTP_KEEP_ALIVE_INTERVAL * 1000);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPClient.SendKeepAlives. " + excp);
            }
        }
    }
}
