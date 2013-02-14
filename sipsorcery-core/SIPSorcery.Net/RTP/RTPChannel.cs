//-----------------------------------------------------------------------------
// Filename: RTPChannel.cs
//
// Description: Communications channel to send and receive RTP packets.
// 
// History:
// 27 Feb 2012	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class RTPChannel
    {
        private static ILog logger = AppState.logger;

        private UDPListener m_rtpListener;
        private IPEndPoint m_localEndPoint;
        private IPEndPoint m_remoteEndPoint;

        private RTPHeader m_sendRTPHeader = new RTPHeader();

        public event Action<byte[], int> SampleReceived;

        public RTPChannel(IPEndPoint localEndPoint)
        {
            m_localEndPoint = localEndPoint;
            m_rtpListener = new UDPListener(localEndPoint);
            m_rtpListener.PacketReceived += RTPPacketReceived;
        }

        public RTPChannel(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint)
        {
            m_localEndPoint = localEndPoint;
            m_remoteEndPoint = remoteEndPoint;
            m_rtpListener = new UDPListener(localEndPoint);
            m_rtpListener.PacketReceived += RTPPacketReceived;
        }

        public void SetRemoteEndPoint(IPEndPoint remoteEndPoint)
        {
            m_remoteEndPoint = remoteEndPoint;
        }

        public void SetSendCodec(RTPPayloadTypesEnum codec)
        {
            m_sendRTPHeader.PayloadType = (int)codec;
        }

        public void Close()
        {
            try
            {
                m_rtpListener.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTPChannel Close. " + excp.Message);
            }
        }

        public void Send(byte[] buffer, uint samplePeriod)
        {
            if (m_remoteEndPoint == null)
            {
                logger.Warn("RTP packet could not be sent as remote end point has not yet been set.");
            }
            else
            {
                m_sendRTPHeader.SequenceNumber++;
                m_sendRTPHeader.Timestamp += samplePeriod;

                RTPPacket rtpPacket = new RTPPacket()
                {
                    Header = m_sendRTPHeader,
                    Payload = buffer
                };

                logger.Debug("Sending RTP packet to " + m_remoteEndPoint + ", seq# " + rtpPacket.Header.SequenceNumber + ", timestamp " + rtpPacket.Header.Timestamp + ".");

                byte[] rtpOut = rtpPacket.GetBytes();
                m_rtpListener.Send(m_remoteEndPoint, rtpOut);
            }
        }

        /// <summary>
        /// Can be used to send non-RTP packets on the RTP socket such as STUN binding request and responses for Gingle.
        /// </summary>
        public void SendRaw(byte[] buffer, int length)
        {
            m_rtpListener.Send(m_remoteEndPoint, buffer, length);
        }

        public void SendRaw(IPEndPoint remoteEndPoint, byte[] buffer, int length)
        {
            m_rtpListener.Send(remoteEndPoint, buffer, length);
        }

        private void RTPPacketReceived(UDPListener listener, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            if ((buffer[0] == 0x0 || buffer[0] == 0x1) && buffer.Length >= 20)
            {
                // Probably a STUN request.
                STUNMessage stunMessage = STUNMessage.ParseSTUNMessage(buffer, buffer.Length);

                if (stunMessage != null)
                {
                    logger.Debug("STUN message received on RTP channel " + stunMessage.Header.MessageType + ".");

                    if (stunMessage.Header.MessageType == STUNMessageTypesEnum.BindingRequest)
                    {
                        logger.Debug("RTP channel sending STUN response to " + remoteEndPoint + ".");
                        STUNMessage stunResponse = new STUNMessage();
                        stunResponse.Header.MessageType = STUNMessageTypesEnum.BindingResponse;
                        stunResponse.Header.TransactionId = stunMessage.Header.TransactionId;
                        stunResponse.AddUsernameAttribute(Encoding.UTF8.GetString(stunMessage.Attributes[0].Value));
                        byte[] stunRespBytes = stunResponse.ToByteBuffer();
                        SendRaw(remoteEndPoint, stunRespBytes, stunRespBytes.Length);
                    }
                }
            }
            else
            {
                logger.Debug("RTP packet received from " + remoteEndPoint + ".");

                if (SampleReceived != null)
                {
                    var rtpHeader = new RTPHeader(buffer);

                    SampleReceived(buffer, rtpHeader.Length);
                }
            }
        }
    }
}
