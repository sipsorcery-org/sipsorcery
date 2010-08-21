//-----------------------------------------------------------------------------
// Filename: SIPConnection.cs
//
// Description: Represents an established socket connection on a connection oriented SIP 
// TCL or TLS.
//
// History:
// 31 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public delegate void SIPConnectionDisconnectedDelegate(IPEndPoint remoteEndPoint);

    public enum SIPConnectionsEnum
    {
        Listener = 1,   // Indicates the connection was initiated by the remote client to a local server socket.
        Caller = 2,     // Indicated the connection was initiated locally to a remote server socket.
    }

    public class SIPConnection
    {
        private static ILog logger = AssemblyState.logger;

        public static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;
        private static string m_sipEOL = SIPConstants.CRLF;
        private static string m_sipMessageDelimiter = SIPConstants.CRLF + SIPConstants.CRLF;

        public Stream SIPStream;
        public Socket SIPSocket;
        public IPEndPoint RemoteEndPoint;
        public SIPProtocolsEnum ConnectionProtocol;
        public SIPConnectionsEnum ConnectionType;
        public DateTime LastTransmission;           // Records when a SIP packet was last sent or received.
        public byte[] SocketBuffer = new byte[2 * MaxSIPTCPMessageSize];
        public int SocketBufferEndPosition = 0;

        private SIPChannel m_owningChannel;

        public event SIPMessageReceivedDelegate SIPMessageReceived;
        public event SIPConnectionDisconnectedDelegate SIPSocketDisconnected = (ep) => { };

        public SIPConnection(SIPChannel channel, Socket sipSocket, IPEndPoint remoteEndPoint, SIPProtocolsEnum connectionProtocol, SIPConnectionsEnum connectionType)
        {
            LastTransmission = DateTime.Now;
            m_owningChannel = channel;
            SIPSocket = sipSocket;
            RemoteEndPoint = remoteEndPoint;
            ConnectionProtocol = connectionProtocol;
            ConnectionType = connectionType;
        }

        public SIPConnection(SIPChannel channel, Stream sipStream, IPEndPoint remoteEndPoint, SIPProtocolsEnum connectionProtocol, SIPConnectionsEnum connectionType)
        {
            LastTransmission = DateTime.Now;
            m_owningChannel = channel;
            SIPStream = sipStream;
            RemoteEndPoint = remoteEndPoint;
            ConnectionProtocol = connectionProtocol;
            ConnectionType = connectionType;
        }

        /// <summary>
        /// Processes the receive buffer after a read from the connected socket.
        /// </summary>
        /// <param name="bytesRead">The number of bytes that were read into the receive buffer.</param>
        /// <returns>True if the receive was processed correctly, false if the socket returned 0 bytes or was disconnected.</returns>
        public bool SocketReadCompleted(int bytesRead)
        {
            try
            {
                if (bytesRead > 0)
                {
                    SocketBufferEndPosition += bytesRead;
                    int bytesSkipped = 0;

                    // Attempt to extract a SIP message from the receive buffer.
                    byte[] sipMsgBuffer = SIPConnection.ProcessReceive(SocketBuffer, 0, SocketBufferEndPosition, out bytesSkipped);

                    while (sipMsgBuffer != null)
                    {
                        // A SIP message is available.
                        if (SIPMessageReceived != null)
                        {
                            LastTransmission = DateTime.Now;
                            SIPMessageReceived(m_owningChannel, new SIPEndPoint(SIPProtocolsEnum.tcp, RemoteEndPoint), sipMsgBuffer);
                        }

                        SocketBufferEndPosition -= (sipMsgBuffer.Length + bytesSkipped);

                        if (SocketBufferEndPosition == 0)
                        {
                            //Array.Clear(SocketBuffer, 0, SocketBuffer.Length);
                            break;
                        }
                        else
                        {
                            // Do a left shift on the receive array.
                            Array.Copy(SocketBuffer, sipMsgBuffer.Length + bytesSkipped, SocketBuffer, 0, SocketBufferEndPosition);
                            //Array.Clear(SocketBuffer, SocketBufferEndPosition, SocketBuffer.Length - SocketBufferEndPosition);

                            // Try and extract another SIP message from the receive buffer.
                            sipMsgBuffer = SIPConnection.ProcessReceive(SocketBuffer, 0, SocketBufferEndPosition, out bytesSkipped);
                        }
                    }

                    return true;
                }
                else
                {
                    //logger.Debug("SIP " + ConnectionProtocol + " socket to " + RemoteEndPoint + " was disconnected, closing.");
                    //SIPStream.Close();
                    Close();
                    SIPSocketDisconnected(RemoteEndPoint);

                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                // Will occur if the owning channel closed the connection.
                SIPSocketDisconnected(RemoteEndPoint);
                return false;
            }
            catch (SocketException)
            {
                // Will occur if the owning channel closed the connection.
                SIPSocketDisconnected(RemoteEndPoint);
                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPConnection SocketReadCompleted. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Processes a buffer from a TCP read operation to extract the first full SIP message. If no full SIP 
        /// messages are available it returns null which indicates the next read should be appended to the current
        /// buffer and the process re-attempted.
        /// </summary>
        /// <param name="receiveBuffer">The buffer to check for the SIP message in.</param>
        /// <param name="start">The position in the buffer to start parsing for a SIP message.</param>
        /// <param name="length">The position in the buffer that indicates the end of the received bytes.</param>
        /// <returns>A byte array holding a full SIP message or if no full SIP messages are avialble null.</returns>
        private static byte[] ProcessReceive(byte[] receiveBuffer, int start, int length, out int bytesSkipped)
        {
            // NAT keep-alives can be interspersed between SIP messages. Treat any non-letter character
            // at the start of a receive as a non SIP transmission and skip over it.
            bytesSkipped = 0;
            bool letterCharFound = false;
            while (!letterCharFound && start < length)
            {
                if ((int)receiveBuffer[start] >= 65)
                {
                    break;
                }
                else
                {
                    start++;
                    bytesSkipped++;
                }
            }

            if (start < length)
            {
                int endMessageIndex = ByteBufferInfo.GetStringPosition(receiveBuffer, start, length, m_sipMessageDelimiter, null);
                if (endMessageIndex != -1)
                {
                    int contentLength = GetContentLength(receiveBuffer, start, endMessageIndex);
                    int messageLength = endMessageIndex - start + m_sipMessageDelimiter.Length + contentLength;

                    if (length - start >= messageLength)
                    {
                        byte[] sipMsgBuffer = new byte[messageLength];
                        Buffer.BlockCopy(receiveBuffer, start, sipMsgBuffer, 0, messageLength);
                        return sipMsgBuffer;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to find the Content-Length header is a SIP header and extract it.
        /// </summary>
        /// <param name="buffer">The buffer to search in.</param>
        /// <param name="start">The position in the buffer to start the serach from.</param>
        /// <param name="end">The position in the buffer to stop the search at.</param>
        /// <returns></returns>
        private static int GetContentLength(byte[] buffer, int start, int end)
        {
            if (buffer == null || start > end || buffer.Length < end)
            {
                return 0;
            }
            else
            {
                byte[] contentHeaderBytes = Encoding.UTF8.GetBytes(m_sipEOL + SIPHeaders.SIP_HEADER_CONTENTLENGTH);
                byte[] compactContentHeaderBytes = Encoding.UTF8.GetBytes(m_sipEOL + SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH);

                int inContentHeaderPosn = 0;
                int inCompactContentHeaderPosn = 0;
                bool possibleHeaderFound = false;
                int contentLengthValueStartPosn = 0;

                for (int index = start; index < end; index++)
                {
                    if (possibleHeaderFound)
                    {
                        // A possilbe match has been found for the Content-Length header. The next characters can only be whitespace or colon.
                        if (buffer[index] == ':')
                        {
                            // The Content-Length header has been found.
                            contentLengthValueStartPosn = index + 1;
                            break;
                        }
                        else if (buffer[index] == ' ' || buffer[index] == '\t')
                        {
                            // Skip any whitespace between the header and the colon.
                            continue;
                        }
                        else
                        {
                            // Additional characters indicate this is not the Content-Length header.
                            possibleHeaderFound = false;
                            inContentHeaderPosn = 0;
                            inCompactContentHeaderPosn = 0;
                        }
                    }

                    if (buffer[index] == contentHeaderBytes[inContentHeaderPosn])
                    {
                        inContentHeaderPosn++;

                        if (inContentHeaderPosn == contentHeaderBytes.Length)
                        {
                            possibleHeaderFound = true;
                        }
                    }
                    else
                    {
                        inContentHeaderPosn = 0;
                    }

                    if (buffer[index] == compactContentHeaderBytes[inCompactContentHeaderPosn])
                    {
                        inCompactContentHeaderPosn++;

                        if (inCompactContentHeaderPosn == compactContentHeaderBytes.Length)
                        {
                            possibleHeaderFound = true;
                        }
                    }
                    else
                    {
                        inCompactContentHeaderPosn = 0;
                    }
                }


                if (contentLengthValueStartPosn != 0)
                {
                    // The Content-Length header has been found, this block extracts the value of the header.
                    string contentLengthValue = null;

                    for (int index = contentLengthValueStartPosn; index < end; index++)
                    {
                        if (contentLengthValue == null && (buffer[index] == ' ' || buffer[index] == '\t'))
                        {
                            // Skip any whitespace at the start of the header value.
                            continue;
                        }
                        else if (buffer[index] >= '0' && buffer[index] <= '9')
                        {
                            contentLengthValue += ((char)buffer[index]).ToString();
                        }
                        else
                        {
                            break;
                        }
                    }


                    if (!contentLengthValue.IsNullOrBlank())
                    {
                        return Convert.ToInt32(contentLengthValue);
                    }
                }

                return 0;
            }
        }

        public void Close()
        {
            try
            {
                if (SIPSocket != null)
                {
                    SIPSocket.Close();
                }
                else
                {
                    SIPStream.Close();
                }
            }
            catch (Exception closeExcp)
            {
                logger.Warn("Exception closing socket in SIPConnection Close. " + closeExcp.Message);
            }
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
        public class SIPConnectionUnitTest
        {
            [TestFixtureSetUp]
            public void Init()
            {
                log4net.Config.BasicConfigurator.Configure();
            }

            [TestFixtureTearDown]
            public void Dispose()
            { }

            [Test]
            public void SampleTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
                Assert.IsTrue(true, "True was false.");
            }

            /// <summary>
            /// Tests that the Content-Length is correctly parsed.
            /// </summary>
            [Test]
            public void ContentLengthParseFromSingleRequestTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string notifyRequest =
@"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport
Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1
To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968
From: <sip:127.0.0.1:5003>;tag=1555449860
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 4 NOTIFY
Content-Length: 2393
Contact: <sip:127.0.0.1:5003>
Max-Forwards: 69
Event: dialog

";
                byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

                int contentLength = GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

                Assert.IsTrue(contentLength == 2393, "The content length was parsed incorrectly.");
            }

            /// <summary>
            /// Tests that the Content-Length is correctly parsed when there is extra spacing in the header.
            /// </summary>
            [Test]
            public void ContentLengthParseFromSingleRequestExtraSpacingTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string notifyRequest =
@"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport
Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1
To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968
From: <sip:127.0.0.1:5003>;tag=1555449860
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 4 NOTIFY
Content-Length      :   2393  
Contact: <sip:127.0.0.1:5003>
Max-Forwards: 69
Event: dialog

";
                byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

                int contentLength = GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

                Assert.IsTrue(contentLength == 2393, "The content length was parsed incorrectly.");
            }

            /// <summary>
            /// Tests that the Content-Length is correctly parsed when a compact header form is used.
            /// </summary>
            [Test]
            public void ContentLengthCompactParseFromSingleRequestTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string notifyRequest =
@"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport
Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1
To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968
From: <sip:127.0.0.1:5003>;tag=1555449860
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 4 NOTIFY
l: 2393
Contact: <sip:127.0.0.1:5003>
Max-Forwards: 69
Event: dialog

";
                byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

                int contentLength = GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

                Assert.IsTrue(contentLength == 2393, "The content length was parsed incorrectly.");
            }

            /// <summary>
            /// Tests that the Content-Length is correctly parsed when a compact header form is used and there is extra
            /// spacing in the header.
            /// </summary>
            [Test]
            public void ContentLengthCompactParseFromSingleRequestExtraSpacingTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string notifyRequest =
@"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport
Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1
To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968
From: <sip:127.0.0.1:5003>;tag=1555449860
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 4 NOTIFY
l   :       2393
Contact: <sip:127.0.0.1:5003>
Max-Forwards: 69
Event: dialog

";
                byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

                int contentLength = GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

                Assert.IsTrue(contentLength == 2393, "The content length was parsed incorrectly.");
            }

            /// <summary>
            /// Tests that a SIP request received with no Content-Length header is interpreted as having no body.
            /// </summary>
            [Test]
            public void ParseReceiveNoContentLengthHeaderRequestTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string notifyRequest =
@"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport
Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1
To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968
From: <sip:127.0.0.1:5003>;tag=1555449860
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 4 NOTIFY
Contact: <sip:127.0.0.1:5003>
Max-Forwards: 69
Event: dialog

";
                byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

                int skippedBytes = 0;
                byte[] parsedNotifyBytes = ProcessReceive(notifyRequestBytes, 0, notifyRequestBytes.Length, out skippedBytes);

                Assert.IsTrue(notifyRequestBytes.Length == parsedNotifyBytes.Length, "The length of the parsed byte array was incorrect.");
            }

            /// <summary>
            /// Tests that a transmission containing a SIP request is correctly extracted.
            /// </summary>
            [Test]
            public void ParseReceiveSingleRequestTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string notifyRequest =
@"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport
Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1
To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968
From: <sip:127.0.0.1:5003>;tag=1555449860
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 4 NOTIFY
Contact: <sip:127.0.0.1:5003>
Max-Forwards: 69
Content-Length: 2393
Event: dialog

<?xml version='1.0' encoding='utf-16'?>
<dialog-info xmlns:ss='sipsorcery:dialog-info' version='0' state='full' entity='sip:aaron@10.1.1.5' xmlns='urn:ietf:params:xml:ns:dialog-info'>
  <dialog id='6eab270b-b981-4734-bb6f-a4d33f77c331' call-id='c0a6182504cd4501afb8339f4218704e' local-tag='1047197926' remote-tag='56F30C5C-4B96DF700001F3A1-B67FABB0' direction='initiator'>
    <state>confirmed</state>
    <duration>1676</duration>
    <ss:bridgeid>1c47e24b-4c1b-4dae-af93-567f26a7c215</ss:bridgeid>
    <local>
      <identity>sip:switchboard@10.1.1.5</identity>
      <cseq>1</cseq>
      <ss:sdp>v=0
o=- 1268178554 1268178554 IN IP4 10.1.1.7
s=Polycom IP Phone
c=IN IP4 10.1.1.7
t=0 0
a=sendrecv
m=audio 2262 RTP/AVP 18 0 8 101
a=rtpmap:18 G729/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:101 telephone-event/8000
</ss:sdp>
    </local>
    <remote>
      <identity>sip:music@iptel.org</identity>
      <target uri='sip:music@213.192.59.78:5080' />
      <cseq>1</cseq>
      <ss:sdp>v=0
o=sems 2134578198 1169488647 IN IP4 213.192.59.78
s=session
c=IN IP4 213.192.59.91
t=0 0
m=audio 27712 RTP/AVP 0 8 101
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
</ss:sdp>
    </remote>
  </dialog>
  <dialog id='b5f20497-c482-4b88-99d1-51b13f9d9167' call-id='4b20b31441064c599e63a3a1320322ae' local-tag='1468371802' remote-tag='1048320465' direction='recipient'>
    <state>confirmed</state>
    <duration>1676</duration>
    <ss:bridgeid>1c47e24b-4c1b-4dae-af93-567f26a7c215</ss:bridgeid>
    <local>
      <identity>sip:hold@10.1.1.5@10.1.1.5</identity>
      <cseq>2</cseq>
      <ss:sdp>v=0
o=sems 2134578198 1169488647 IN IP4 213.192.59.78
s=session
c=IN IP4 213.192.59.91
t=0 0
m=audio 27712 RTP/AVP 0 8 101
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
</ss:sdp>
    </local>
    <remote>
      <identity>sip:switchboard@10.1.1.5</identity>
      <target uri='sip:10.1.1.5:62442;transport=tcp' />
      <cseq>2</cseq>
      <ss:sdp>v=0
o=- 1268178554 1268178554 IN IP4 10.1.1.7
s=Polycom IP Phone
c=IN IP4 10.1.1.7
t=0 0
a=sendrecv
m=audio 2262 RTP/AVP 18 0 8 101
a=rtpmap:18 G729/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:101 telephone-event/8000
</ss:sdp>
    </remote>
  </dialog>
</dialog-info>";

                byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

                int skippedBytes = 0;
                byte[] parsedNotifyBytes = ProcessReceive(notifyRequestBytes, 0, notifyRequestBytes.Length, out skippedBytes);

                Assert.IsTrue(notifyRequestBytes.Length == parsedNotifyBytes.Length, "The length of the parsed byte array was incorrect.");
            }

            /// <summary>
            /// Tests parsing a receive with multiple requests and responses.
            /// </summary>
            [Test]
            public void ParseMultiRequestAndResponseTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
                
                string testReceive =
                @"SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=trueSUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bK82b1f0704fc31f47b4c9e0bc383d3e0e41f2a60f;rport
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bK6d88a47e4b5c4bde9c45270ca64a1c53;rport
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport=62647;received=10.1.1.5
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 69
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog
Proxy-ReceivedFrom: tcp:10.1.1.5:62647
Proxy-ReceivedOn: tcp:10.1.1.5:4506

includesdp=trueSIP/2.0 200 Ok
Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKba4e75d7c55baef96457b36b7b570dae9a253dd8;rport=5060;received=127.0.0.1
Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKc6f4c0fcd4684246abf539848017c0f0;rport
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bK17bbf15513b44e6aa88b605410148d2b;rport=62647;received=10.1.1.5
To: <sip:switchboard@10.1.1.5>;tag=2140367015
From: <sip:switchboard@10.1.1.5>;tag=1557768010
Call-ID: a65b4461-6929-4604-b498-256f6643e6ac
CSeq: 2 REGISTER
Contact: <sip:10.1.1.5:62647;transport=tcp>;expires=113
Date: Wed, 10 Mar 2010 00:21:14 GMT
Content-Length: 0
Server: www.sipsorcery.com

";
                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                int skippedBytes = 0;
                byte[] request1Bytes = ProcessReceive(testReceiveBytes, 0, testReceiveBytes.Length, out skippedBytes);
                Console.WriteLine("Request1=" + UTF8Encoding.UTF8.GetString(request1Bytes));

                byte[] request2Bytes = ProcessReceive(testReceiveBytes, request1Bytes.Length, testReceiveBytes.Length, out skippedBytes);
                Console.WriteLine("Request2=" + UTF8Encoding.UTF8.GetString(request2Bytes));

                byte[] response1Bytes = ProcessReceive(testReceiveBytes, request1Bytes.Length + request2Bytes.Length, testReceiveBytes.Length, out skippedBytes);
                Console.WriteLine("Response1=" + UTF8Encoding.UTF8.GetString(response1Bytes));

                Assert.IsTrue(request1Bytes.Length + request2Bytes.Length + response1Bytes.Length == testReceiveBytes.Length, "The length of the parsed requests and responses was incorrect.");
            }

            /// <summary>
            /// Test that parsing a request with a single byte missing from its content is correcty handled.
            /// </summary>
            [Test]
            public void ParseRequestOneByteMissingTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
                @"SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=tru";
                
                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                int skippedBytes = 0;
                byte[] request1Bytes = ProcessReceive(testReceiveBytes, 0, testReceiveBytes.Length, out skippedBytes);

                Assert.IsNull(request1Bytes, "The parsed bytes should have been empty.");
            }

            /// <summary>
            /// Test that parsing a request with a single byte missing from its content is correcty handled.
            /// </summary>
            [Test]
            public void ParseRequestOneByteExtraTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
                @"SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=true!";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                int skippedBytes = 0;
                byte[] request1Bytes = ProcessReceive(testReceiveBytes, 0, testReceiveBytes.Length, out skippedBytes);

                Assert.IsTrue(request1Bytes.Length == testReceiveBytes.Length - 1, "The parsed bytes was an incorrect length.");
            }

            /// <summary>
            /// Test parsing a request where the array contains enough data but the end position of the valid data in the array is short.
            /// This will occur when using a fixed length buffer to receive data and the position of the received data is less than the length
            /// of the receive array.
            /// </summary>
            [Test]
            public void ParseRequestBytesReadShortTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
                @"SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

include                                               ";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                int skippedBytes = 0;
                byte[] request1Bytes = ProcessReceive(testReceiveBytes, 0, testReceiveBytes.Length - 100, out skippedBytes);

                Assert.IsNull(request1Bytes, "A request array should not have been returned.");
            }

            /// <summary>
            /// Test that parsing a request works when there are some leading bytes related to a NAT keep alive transmission.
            /// </summary>
            [Test]
            public void ParseRequestWithLeadingNATKeepAliveBytesTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
                @"    SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=true";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                int skippedBytes = 0;
                byte[] request1Bytes = ProcessReceive(testReceiveBytes, 0, testReceiveBytes.Length, out skippedBytes);

                Console.WriteLine(Encoding.UTF8.GetString(request1Bytes));

                Assert.IsNotNull(request1Bytes, "The parsed bytes should have been populated.");
                Assert.IsTrue(skippedBytes == 4, "The number of skipped bytes was incorrect.");
            }

            /// <summary>
            /// Tests that a socket read leaves the buffers and positions in the correct state.
            /// </summary>
            [Test]
            public void TestSocketReadSingleMessageTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
@"SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=true";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                SIPConnection testConnection = new SIPConnection(null, (Stream)null, null, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Caller);
                Array.Copy(testReceiveBytes, testConnection.SocketBuffer, testReceiveBytes.Length);

                bool result = testConnection.SocketReadCompleted(testReceiveBytes.Length);

                Assert.IsTrue(result, "The result of processing the receive should have been true.");
                Assert.IsTrue(testConnection.SocketBufferEndPosition == 0, "The receive buffer end position should have been 0.");
            }

            /// <summary>
            /// Tests that processing a buffer with a SIP message and some preceeding spurious characters skips the correct number of bytes.
            /// </summary>
            [Test]
            public void TestProcessRecevieWithBytesToSkipTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
@"            SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=true";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                int bytesSkipped = 0;
                byte[] result = SIPConnection.ProcessReceive(testReceiveBytes, 0, testReceiveBytes.Length, out bytesSkipped);

                Assert.IsNotNull(result, "The resultant array should not have been null.");
                Assert.IsTrue(bytesSkipped == 12, "The bytes skipped was incorrect.");
            }

            /// <summary>
            /// Tests that a socket read leaves the buffers and positions in the correct state when the SIP message has spurious characters
            /// preceeding the transmission.
            /// </summary>
            [Test]
            public void TestSocketReadWithBytesToSkipTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
@"            SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=true";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                SIPConnection testConnection = new SIPConnection(null, (Stream)null, new IPEndPoint(IPAddress.Loopback, 0), SIPProtocolsEnum.tcp, SIPConnectionsEnum.Caller);
                int sipMessages = 0;
                testConnection.SIPMessageReceived += (chan, ep, buffer) => { sipMessages++; };
                Array.Copy(testReceiveBytes, testConnection.SocketBuffer, testReceiveBytes.Length);

                bool result = testConnection.SocketReadCompleted(testReceiveBytes.Length);

                Assert.IsTrue(result, "The result from processing the socket read should have been true.");
                Assert.IsTrue(sipMessages == 1, "The number of SIP messages parsed was incorrect.");
                Assert.IsTrue(testConnection.SocketBufferEndPosition == 0, "The receive buffer end position was incorrect.");
            }

            /// <summary>
            /// Tests that a socket read leaves the buffers and positions in the correct state when the receive contains multiple 
            /// SIP message has spurious characters preceeding the transmission.
            /// </summary>
            [Test]
            public void TestSocketReadWithTwoMessagesAndBytesToSkipTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testReceive =
@"            SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE
Contact: <sip:10.1.1.5:62647;transport=tcp>
Max-Forwards: 70
Expires: 600
Content-Length: 15
Content-Type: text/text
Event: dialog

includesdp=true       

 
 SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0
Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport
To: <sip:aaron@10.1.1.5>
From: <sip:switchboard@10.1.1.5>;tag=1902440575
Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a
CSeq: 3 SUBSCRIBE

SUBSCRIBE sip:aaron@10.1.1";

                byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

                SIPConnection testConnection = new SIPConnection(null, (Stream)null, new IPEndPoint(IPAddress.Loopback, 0), SIPProtocolsEnum.tcp, SIPConnectionsEnum.Caller);
                int sipMessages = 0;
                testConnection.SIPMessageReceived += (chan, ep, buffer) => { sipMessages++; };
                Array.Copy(testReceiveBytes, testConnection.SocketBuffer, testReceiveBytes.Length);

                bool result = testConnection.SocketReadCompleted(testReceiveBytes.Length);
                string remainingBytes =  Encoding.UTF8.GetString(testConnection.SocketBuffer, 0, testConnection.SocketBufferEndPosition);

                Console.WriteLine("SocketBufferEndPosition=" + testConnection.SocketBufferEndPosition + ".");
                Console.WriteLine("SocketBuffer=" + remainingBytes + ".");

                Assert.IsTrue(result, "The result from processing the socket read should have been true.");
                Assert.IsTrue(sipMessages == 2, "The number of SIP messages parsed was incorrect.");
                Assert.IsTrue(testConnection.SocketBufferEndPosition == 26, "The receive buffer end position was incorrect.");
                Assert.IsTrue(remainingBytes == "SUBSCRIBE sip:aaron@10.1.1", "The leftover bytes in the socket buffer were incorrect.");
            }
        }

        #endif

        #endregion
    }
}
