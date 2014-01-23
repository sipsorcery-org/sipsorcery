//-----------------------------------------------------------------------------
// Filename: RTSPSession.cs
//
// Description: Represents an active RTSP session for an RTSP server process that is sending RTP packets
// to a client.
// 
// History:
// 23 Jan 2014	Aaron Clauson	Created. Borrowed bits from https://net7mma.codeplex.com (license @ https://net7mma.codeplex.com/license).
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class RTSPSession
    {
        private const int RFC_2435_FREQUENCY_BASELINE = 90000;
        private const int RTP_MAX_PAYLOAD = 1452;

        private static DateTime UtcEpoch2036 = new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc);
        private static DateTime UtcEpoch1900 = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime UtcEpoch1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ILog logger = AppState.logger;

        private int _rtpPort;
        private int _controlPort;
        private IPEndPoint _remoteEndPoint;
        private Socket _rtpSocket;
        private Socket _controlSocket;
        private bool _hasRTPSocketError = false;

        private string _sessionID;
        public string SessionID
        {
            get { return _sessionID; }
        }

        public bool IsClosed
        {
            get { return _hasRTPSocketError; }
        }

        // Fields that track the RTP stream being managed in this session.
        private ushort _sequenceNumber = 1;
        private uint _timestamp = 0;
        private uint _syncSource = 0;

        public RTSPSession(string sessionID, int rtpPort, int controlPort, IPEndPoint remoteEndPoint)
        {
            _sessionID = sessionID;
            _rtpPort = rtpPort;
            _controlPort = controlPort;
            _remoteEndPoint = remoteEndPoint;
            _syncSource = Convert.ToUInt32(Crypto.GetRandomInt(0, 9999999));
        }

        public void Start()
        {
            _rtpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _rtpSocket.Bind(new IPEndPoint(IPAddress.Any, _rtpPort));
        }

        public void Close()
        {
            try
            {
                if (_rtpSocket != null)
                {
                    _rtpSocket.Close();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPSession.Close. " + excp);
            }
        }

        /// <summary>
        /// Helper method to send a low quality JPEG image over RTP. This method supports a very abbreviated version of RFC 2435 "RTP Payload Format for JPEG-compressed Video".
        /// It's intended as a quick convenient way to send something like a test pattern image over an RTSP connection. More than likely it won't be suitable when a high
        /// quality image is required since the header used in htis method does not support quantization tables.
        /// </summary>
        /// <param name="jpegBytes">The raw encoded bytes of teh JPEG image to transmit.</param>
        /// <param name="jpegQuality">The encoder quality of the JPEG image.</param>
        /// <param name="jpegWidth">The width of the JPEG image.</param>
        /// <param name="jpegHeight">The height of the JPEG image.</param>
        /// <param name="framesPerSecond">The rate at which the JPEG frames are being transmitted at. used to calculate the timestamp.</param>
        public void SendJpegFrame(byte[] jpegBytes, int jpegQuality, int jpegWidth, int jpegHeight, int framesPerSecond)
        {
            _timestamp = (_timestamp == 0) ? DateTimeToNptTimestamp32(DateTime.Now) : (_timestamp + (uint)(RFC_2435_FREQUENCY_BASELINE / framesPerSecond)) % UInt32.MaxValue;

            //System.Diagnostics.Debug.WriteLine("Sending " + jpegBytes.Length + " encoded bytes to client, timestamp " + _timestamp + ", starting sequence number " + _sequenceNumber + ", image dimensions " + jpegWidth + " x " + jpegHeight + ".");

            for (int index = 0; index * RTP_MAX_PAYLOAD < jpegBytes.Length; index++)
            {
                uint offset = Convert.ToUInt32(index * RTP_MAX_PAYLOAD);
                int payloadLength = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? RTP_MAX_PAYLOAD : jpegBytes.Length - index * RTP_MAX_PAYLOAD;

                byte[] jpegHeader = CreateLowQualityRtpJpegHeader(offset, jpegQuality, jpegWidth, jpegHeight);

                List<byte> packetPayload = new List<byte>();
                packetPayload.AddRange(jpegHeader);
                packetPayload.AddRange(jpegBytes.Skip(index * RTP_MAX_PAYLOAD).Take(payloadLength));

                RTPPacket rtpPacket = new RTPPacket(packetPayload.Count);
                rtpPacket.Header.SyncSource = _syncSource;
                rtpPacket.Header.SequenceNumber = _sequenceNumber++;
                rtpPacket.Header.Timestamp = _timestamp;
                rtpPacket.Header.MarkerBit = ((index + 1) * RTP_MAX_PAYLOAD < jpegBytes.Length) ? 0 : 1;
                rtpPacket.Header.PayloadType = (int)SDPMediaFormatsEnum.JPEG;
                rtpPacket.Payload = packetPayload.ToArray();

                byte[] rtpBytes = rtpPacket.GetBytes();

                //System.Diagnostics.Debug.WriteLine(" offset " + offset + ", payload length " + payloadLength + ", sequence number " + rtpPacket.Header.SequenceNumber + ", marker " + rtpPacket.Header.MarkerBit + ".");

                try
                {
                    _rtpSocket.SendTo(rtpBytes, _remoteEndPoint);
                }
                catch (Exception excp)
                {
                    logger.Warn("Exception RTPSession.SendJpegFrame attempting to send to the RTP socket at " + _remoteEndPoint + ". " + excp);
                    _hasRTPSocketError = true;
                }
            }
        }

        public static uint DateTimeToNptTimestamp32(DateTime value) { return (uint)((DateTimeToNptTimestamp(value) >> 16) & 0xFFFFFFFF); }

        /// <summary>
        /// Converts specified DateTime value to long NPT time.
        /// </summary>
        /// <param name="value">DateTime value to convert. This value must be in local time.</param>
        /// <returns>Returns NPT value.</returns>
        /// <notes>
        /// Wallclock time (absolute date and time) is represented using the
        /// timestamp format of the Network Time Protocol (NPT), which is in
        /// seconds relative to 0h UTC on 1 January 1900 [4].  The full
        /// resolution NPT timestamp is a 64-bit unsigned fixed-point number with
        /// the integer part in the first 32 bits and the fractional part in the
        /// last 32 bits. In some fields where a more compact representation is
        /// appropriate, only the middle 32 bits are used; that is, the low 16
        /// bits of the integer part and the high 16 bits of the fractional part.
        /// The high 16 bits of the integer part must be determined independently.
        /// </notes>
        public static ulong DateTimeToNptTimestamp(DateTime value)
        {
            DateTime baseDate = value >= UtcEpoch2036 ? UtcEpoch2036 : UtcEpoch1900;

            TimeSpan elapsedTime = value > baseDate ? value.ToUniversalTime() - baseDate.ToUniversalTime() : baseDate.ToUniversalTime() - value.ToUniversalTime();

            return ((ulong)(elapsedTime.Ticks / TimeSpan.TicksPerSecond) << 32) | (uint)(elapsedTime.Ticks / TimeSpan.TicksPerSecond * 0x100000000L);
        }

        /// <summary>
        /// Utility function to create RtpJpegHeader either for initial packet or template for further packets
        /// 
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// | Type-specific |              Fragment Offset                  |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// |      Type     |       Q       |     Width     |     Height    |
        /// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        /// </summary>
        /// <param name="fragmentOffset"></param>
        /// <param name="quality"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private static byte[] CreateLowQualityRtpJpegHeader(uint fragmentOffset, int quality, int width, int height)
        {
            byte[] rtpJpegHeader = new byte[8] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };

            // Byte 0: Type specific
            //http://tools.ietf.org/search/rfc2435#section-3.1.1

            // Bytes 1 to 3: Three byte fragment offset
            //http://tools.ietf.org/search/rfc2435#section-3.1.2

            if (BitConverter.IsLittleEndian) fragmentOffset = NetConvert.DoReverseEndian(fragmentOffset);

            byte[] offsetBytes = BitConverter.GetBytes(fragmentOffset);
            rtpJpegHeader[1] = offsetBytes[2];
            rtpJpegHeader[2] = offsetBytes[1];
            rtpJpegHeader[3] = offsetBytes[0];

            // Byte 4: JPEG Type.
            //http://tools.ietf.org/search/rfc2435#section-3.1.3

            //Byte 5: http://tools.ietf.org/search/rfc2435#section-3.1.4 (Q)
            rtpJpegHeader[5] = (byte)quality;

            // Byte 6: http://tools.ietf.org/search/rfc2435#section-3.1.5 (Width)
            rtpJpegHeader[6] = (byte)(width / 8);

            // Byte 7: http://tools.ietf.org/search/rfc2435#section-3.1.6 (Height)
             rtpJpegHeader[7] = (byte)(height / 8);

            return rtpJpegHeader;
        }
    }
}
