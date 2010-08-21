//-----------------------------------------------------------------------------
// Filename: RTPTestStreamSink.cs
//
// Description: Sink for sending RTP streams to for diagnsotic or measuremnent
// purposes.
//
// History:
// 24 May 2005	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
	public delegate void RTPSinkClosed(Guid streamId, Guid callId);
	public delegate void RTPDataReceived(Guid streamId, byte[] rtpPayload, IPEndPoint remoteEndPoint);
	public delegate void RTPDataSent(Guid streamId, byte[] rtpPayload, IPEndPoint remoteEndPoint);
	public delegate void RTPRemoteEndPointChanged(Guid streamId, IPEndPoint remoteEndPoint);
    public delegate void RTCPSampleReadyDelegate(RTCPReport rtcpReport);
    public delegate void RTCPReportRecievedDelegate(RTPSink rtpSink, RTCPReportPacket rtcpReportPacket);
	
	/// <summary>
	/// Threads used:
    /// 1. ListenerThread to listen for incoming RTP packets on the listener socket,
    /// 2. ListenerTimeoutThread shuts down the stream if a packet is not received in NO_RTP_TIMEOUT or if the stream's lifetime 
    ///    exceeds RTPMaxStayAlive (and RTPMaxStayAlive has been set > 0),
    /// 3. SenderThread sends RTP packets to remote socket.
	/// </summary>
    public class RTPSink
	{
		public const int RTP_PORTRANGE_START = 12000;
		public const int RTP_PORTRANGE_END = 17000;
		public const int NO_RTP_TIMEOUT = 30;			// Seconds of no RTP after which the connection will be closed.
		public const int RTCP_SAMPLE_WINDOW = 5;		// Number of seconds at which to rollover RTCP measurements
		public const int RTP_HEADER_SIZE = 12;			// Number of bytes used in RTP packet for RTP header.
		public const int RTP_BASEFRAME_SIZE = 15;		// This should only be set in increments of 15ms because the timing resolution of Windows/.Net revolves around this figure. This means
														// that even if it is set at 10, 20 or 40 it will ultimately end up being more like 15, 30 or 60.
		public const int RTP_DEFAULTPACKET_SIZE = 160;	// Corresponds to the default payload size for a 20ms g711 packet. Not a good idea to go 
														// over 1460 as Ethernet MTU = 1500. (12B RTP header, 8B UDP Header, 20B IP Header, TOTAL = 40B).
		//public const int DATAGRAM_MAX_SIZE = 1460;
        public const int TIMESTAMP_FACTOR = 100;
        public const int TYPEOFSERVICE_RTPSEND = 47;

        private static string m_typeOfService = AppState.GetConfigSetting("RTPPacketTypeOfService");
				
		private ArrayList m_rtpChannels = new ArrayList();	// Use SetRTPChannels to adjust. List of the current RTPHeader's representing an 
															// individual RTP stream being sent between the two end points on this sink.
															// It is the number of calls to emulate (each channel will be RTPFrameSize ms frames at RTPPacketSendSize of data). Only one channel
															// is used to take packet interarrival measurements from but the data transfer rates are measurments across all data.
		
		private int m_rtpPacketSendSize = RTP_DEFAULTPACKET_SIZE;	// Bytes of data to put in RTP packet payload.
		public int RTPPacketSendSize
		{
			get{ return m_rtpPacketSendSize; }
			set
			{
				if(value < 0)
				{
					m_rtpPacketSendSize = 0;
				}
				else
				{
                    m_rtpPacketSendSize = value;

                    /*if(value < DATAGRAM_MAX_SIZE)
					{
						m_rtpPacketSendSize = value;
					}
					else
					{
						m_rtpPacketSendSize = DATAGRAM_MAX_SIZE;
					}*/
				}
			}
		}
		public int RTPFrameSize = RTP_BASEFRAME_SIZE;
		public int RTPMaxStayAlive = 0;					// If > 0 this specifies the maximum number of seconds the RTP stream will send for, after that time it will self destruct.
		private int m_channels = 1;						// This is a feature that will burst each RTP packet m_channels times, it's a way of duplicating simultaneous calls.
		public int RTPChannels
		{
			get{ return m_channels; }
			set
			{ 
				if(value < 1)
				{
					logger.Info("Changing RTP channels from " + m_channels + " to 1, requested value was " + value + ".");
					m_channels = 1;
				}
				else if(value != m_channels)
				{
					logger.Info("Changing RTP channels from " + m_channels + " to " + value + ".");
					m_channels = value;
				}
			}
		}

        private static ILog logger = AppState.GetLogger("rtp");

		private IPEndPoint m_streamEndPoint;

		private DateTime m_lastRTPReceivedTime = DateTime.MinValue;
		private DateTime m_lastRTPSentTime = DateTime.MinValue;
		private DateTime m_startRTPReceiveTime = DateTime.MinValue;
		private DateTime m_startRTPSendTime = DateTime.MinValue;

		// Used to time the receiver to close the connection if no data is received during a certain amount of time (NO_RTP_TIMEOUT).
		private ManualResetEvent m_lastPacketReceived = new ManualResetEvent(false);

		private UdpClient m_udpListener;
		private IPEndPoint m_localEndPoint;
		//private uint m_syncSource;

		public bool StopListening = false;
		public bool ShuttingDown = false;
		public bool Sending = false;
		public bool LogArrivals = false;		// Whether to log packet arrival events.
        public bool StopIfNoData = true;

		public event RTPSinkClosed ListenerClosed;
		public event RTPSinkClosed SenderClosed;
		public event RTPDataReceived DataReceived;
		public event RTPDataSent DataSent;
		public event RTPRemoteEndPointChanged RemoteEndPointChanged;
        public event RTCPSampleReadyDelegate RTCPReportReady;
        public event RTCPReportRecievedDelegate RTCPReportReceived;

		private RTCPReportSampler m_rtcpSampler = null;
        public uint LastReceivedReportNumber = 0;       // The report number of the last RTCP report received from the remote end.

        #region Properties.

        private Guid m_streamId = Guid.NewGuid();
		public Guid StreamId
		{
			get{ return m_streamId; }
		}
		
		private Guid m_callDescriptorId;
		public Guid CallDescriptorId
		{
			get{ return m_callDescriptorId; }
			set{ m_callDescriptorId = value; }
		}

		private long m_packetsSent = 0;
		public long PacketsSent
		{
			get{ return m_packetsSent; }
		}

		private long m_packetsReceived = 0;
		public long PacketsReceived
		{
			get{ return m_packetsReceived; }
		}

		private long m_bytesSent = 0;
		public long BytesSent
		{
			get{ return m_bytesSent; }
		}
		private long m_bytesReceived = 0;
		public long BytesReceived
		{
			get{ return m_bytesReceived; }
		}

		//private MemoryStream m_rtpStream = new MemoryStream();

        // Info only variables to validate what the RCTP report is producing.
        //private DateTime m_sampleStartTime = DateTime.MinValue;
        //private int m_samplePackets;
        //private int m_sampleBytes;
        private UInt16 m_sampleStartSeqNo;
		
		public int ListenPort
		{
			get{ return m_localEndPoint.Port; }
		}
	
		public IPEndPoint ClientEndPoint
		{
			get{ return m_localEndPoint; }
		}

		public IPEndPoint RemoteEndPoint
		{
			get{ return m_streamEndPoint; }
        }

        #endregion

        public RTPSink(IPAddress localAddress, ArrayList inUsePorts)
		{
            m_udpListener = NetServices.CreateRandomUDPListener(localAddress, RTP_PORTRANGE_START, RTP_PORTRANGE_END, inUsePorts, out m_localEndPoint);

            int typeOfService = TYPEOFSERVICE_RTPSEND;
            // If a setting has been supplied in the config file use that.
            Int32.TryParse(m_typeOfService, out typeOfService);

            try
            {
                m_udpListener.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, typeOfService);
                logger.Debug("Setting RTP packet ToS to " + m_typeOfService + ".");
            }
            catch (Exception excp)
            {
                logger.Warn("Exception setting IP type of service for RTP packet to " + typeOfService + ". " + excp.Message);
            }

			logger.Info("RTPSink established on " + m_localEndPoint.Address.ToString() + ":" + m_localEndPoint.Port + ".");
			//receiveLogger.Info("Send Time,Send Timestamp,Receive Time,Receive Timestamp,Receive Offset(ms),Timestamp Diff,SeqNum,Bytes");
			//sendLogger.Info("Send Time,Send Timestamp,Send Offset(ms),SeqNum,Bytes");
		}

		public RTPSink(IPEndPoint localEndPoint)
		{
			m_localEndPoint = localEndPoint;

			m_udpListener = new UdpClient(m_localEndPoint);

            int typeOfService = TYPEOFSERVICE_RTPSEND;
            // If a setting has been supplied in the config file use that.
            Int32.TryParse(m_typeOfService, out typeOfService);
            
            try
            {
                m_udpListener.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, typeOfService);
                logger.Debug("Setting RTP packet ToS to " + m_typeOfService + ".");
            }
            catch (Exception excp)
            {
                logger.Warn("Exception setting IP type of service for RTP packet to " + typeOfService + ". " + excp.Message);
            }
			
            logger.Info("RTPSink established on " + m_localEndPoint.Address.ToString() + ":" + m_localEndPoint.Port + ".");
			//receiveLogger.Info("Receive Time,Receive Offset (ms),SeqNum,Bytes");
			//sendLogger.Info("Send Time,Send Offset (ms),SeqNum,Bytes");
		}

		public void StartListening()
		{
			try
			{
				Thread rtpListenerThread = new Thread(new ThreadStart(Listen));
				rtpListenerThread.Start();

                if (StopIfNoData)
                {
                    // If the stream should be shutdown after receiving no data for the timeout period then this thread gets started to mointor receives.
                    Thread rtpListenerTimeoutThread = new Thread(new ThreadStart(ListenerTimeout));
                    rtpListenerTimeoutThread.Start();
                }
			}
			catch(Exception excp)
			{
				logger.Error("Exception Starting RTP Listener Threads. " + excp.Message);
				throw excp;
			}
		}
	
		private void Listen()
		{
            try
            {
                UdpClient udpSvr = m_udpListener;

                if (udpSvr == null)
                {
                    logger.Error("The UDP server was not correctly initialised in the RTP sink when attempting to start the listener, the RTP stream has not been intialised.");
                    return;
                }
                else
                {
                    logger.Debug("RTP Listener now listening on " + m_localEndPoint.Address + ":" + m_localEndPoint.Port + ".");
                }

                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] rcvdBytes = null;

                m_startRTPReceiveTime = DateTime.MinValue;
                m_lastRTPReceivedTime = DateTime.MinValue;
                DateTime previousRTPReceiveTime = DateTime.MinValue;
                uint previousTimestamp = 0;
                UInt16 sequenceNumber = 0;
                UInt16 previousSeqNum = 0;
                uint senderSendSpacing = 0;
                uint lastSenderSendSpacing = 0;

                while (!StopListening)
                {
                    rcvdBytes = null;

                    try
                    {
                        rcvdBytes = udpSvr.Receive(ref remoteEndPoint);
                    }
                    catch
                    {
                        //logger.Warn("Remote socket closed on receive. Last RTP received " + m_lastRTPReceivedTime.ToString("dd MMM yyyy HH:mm:ss") + ", last RTP successfull send " +  m_lastRTPSentTime.ToString("dd MMM yyyy HH:mm:ss") + ".");
                    }

                    if (rcvdBytes != null && rcvdBytes.Length > 0)
                    {
                        // Check whether this is an RTCP report.
                        UInt16 firstWord = BitConverter.ToUInt16(rcvdBytes, 0);
                        if (BitConverter.IsLittleEndian)
                        {
                            firstWord = NetConvert.DoReverseEndian(firstWord);
                        }

                        ushort packetType = 0;
                        if (BitConverter.IsLittleEndian)
                        {
                            packetType = Convert.ToUInt16(firstWord & 0x00ff);
                        }

                       if (packetType == RTCPHeader.RTCP_PACKET_TYPE)
                        {
                            logger.Debug("RTP Listener received remote RTCP report from " + remoteEndPoint + ".");

                            try
                            {
                                RTCPPacket rtcpPacket = new RTCPPacket(rcvdBytes);
                                RTCPReportPacket rtcpReportPacket = new RTCPReportPacket(rtcpPacket.Reports);

                                if (RTCPReportReceived != null)
                                {
                                    RTCPReportReceived(this, rtcpReportPacket);
                                }
                            }
                            catch (Exception rtcpExcp)
                            {
                                logger.Error("Exception processing remote RTCP report. " + rtcpExcp.Message);
                            }

                            continue;
                        }

                        // Channel statistics.
                        DateTime rtpReceiveTime = DateTime.Now;
                        if (m_startRTPReceiveTime == DateTime.MinValue)
                        {
                            m_startRTPReceiveTime = rtpReceiveTime;
                            //m_sampleStartTime = rtpReceiveTime;
                        }
                        previousRTPReceiveTime = new DateTime(m_lastRTPReceivedTime.Ticks);
                        m_lastRTPReceivedTime = rtpReceiveTime;
                        m_packetsReceived++;
                        m_bytesReceived += rcvdBytes.Length;

                        previousSeqNum = sequenceNumber; 

                        // This stops the thread running the ListenerTimeout method from deciding the strema has recieved no RTP and therefore should be shutdown.
                        m_lastPacketReceived.Set();

                        // Let whoever has subscribed that an RTP packet has been received.
                        if (DataReceived != null)
                        {
                            try
                            {
                                DataReceived(m_streamId, rcvdBytes, remoteEndPoint);
                            }
                            catch (Exception excp)
                            {
                                logger.Error("Exception RTPSink DataReceived. " + excp.Message);
                            }
                        }

                        if (m_packetsReceived % 500 == 0)
                        {
                            logger.Debug("Total packets received from " + remoteEndPoint.ToString() + " " + m_packetsReceived + ", bytes " + NumberFormatter.ToSIByteFormat(m_bytesReceived, 2) + ".");
                        }

                        try
                        {
                            RTPPacket rtpPacket = new RTPPacket(rcvdBytes);
                            uint syncSource = rtpPacket.Header.SyncSource;
                            uint timestamp = rtpPacket.Header.Timestamp;
                            sequenceNumber = rtpPacket.Header.SequenceNumber;

                            //logger.Debug("seqno=" + rtpPacket.Header.SequenceNumber + ", timestamp=" + timestamp);

                            if (previousRTPReceiveTime != DateTime.MinValue)
                            {
                                //uint senderSendSpacing = rtpPacket.Header.Timestamp - previousTimestamp;
                                // Need to cope with cases where the timestamp has looped, if this timestamp is < last timesatmp and there is a large difference in them then it's because the timestamp counter has looped.
                                lastSenderSendSpacing = senderSendSpacing;
                                senderSendSpacing = (Math.Abs(timestamp - previousTimestamp) > (uint.MaxValue / 2)) ? timestamp + uint.MaxValue - previousTimestamp : timestamp - previousTimestamp;

                                if (previousTimestamp > timestamp)
                                {
                                    logger.Error("BUG: Listener previous timestamp (" + previousTimestamp + ") > timestamp (" + timestamp + "), last seq num=" + previousSeqNum + ", seqnum=" + sequenceNumber + ".");
                                    
                                    // Cover for this bug until it's nailed down.
                                    senderSendSpacing = lastSenderSendSpacing;
                                }

                                double senderSpacingMilliseconds = (double)senderSendSpacing / (double)TIMESTAMP_FACTOR;
                                double interarrivalReceiveTime = m_lastRTPReceivedTime.Subtract(previousRTPReceiveTime).TotalMilliseconds;

                                #region RTCP reporting.

                                if (m_rtcpSampler == null)
                                {
                                    //resultsLogger.Info("First Packet: " + rtpPacket.Header.SequenceNumber + "," + m_arrivalTime.ToString("HH:mm:fff"));

                                    m_rtcpSampler = new RTCPReportSampler(m_streamId, syncSource, remoteEndPoint, rtpPacket.Header.SequenceNumber, m_lastRTPReceivedTime, rcvdBytes.Length);
                                    m_rtcpSampler.RTCPReportReady += new RTCPSampleReadyDelegate(m_rtcpSampler_RTCPReportReady);
                                    m_rtcpSampler.StartSampling();
                                }
                                else
                                {
                                    //m_receiverReports[syncSource].RecordRTPReceive(rtpPacket.Header.SequenceNumber, sendTime, rtpReceiveTime, rcvdBytes.Length);
                                    // Transit time is calculated by knowing that the sender sent a packet at a certain time after the last send and the receiver received a pakcet a certain time after the last receive.
                                    // The difference in these two times is the jitter present. The transit time can change with each transimission and as this methid relies on two sends two packet
                                    // arrivals to calculate the transit time it's not going to be perfect (you'd need synchronised NTP clocks at each end to be able to be accurate).
                                    // However if used tor an average calculation it should be pretty close.
                                    //double transitTime = Math.Abs(interarrivalReceiveTime - senderSpacingMilliseconds);
                                    uint jitter = (interarrivalReceiveTime - senderSpacingMilliseconds > 0) ? Convert.ToUInt32(interarrivalReceiveTime - senderSpacingMilliseconds) : 0;

                                    if(jitter > 75)
                                    {
                                        logger.Debug("seqno=" + rtpPacket.Header.SequenceNumber + ", timestmap=" + timestamp + ", ts-prev=" + previousTimestamp + ", receive spacing=" + interarrivalReceiveTime + ", send spacing=" + senderSpacingMilliseconds + ", jitter=" + jitter);
                                    }
                                    else
                                    {
                                        //logger.Debug("seqno=" + rtpPacket.Header.SequenceNumber + ", receive spacing=" + interarrivalReceiveTime + ", timestamp=" + timestamp + ", transit time=" + transitTime);
                                    }

                                    m_rtcpSampler.RecordRTPReceive(m_lastRTPReceivedTime, rtpPacket.Header.SequenceNumber, rcvdBytes.Length, jitter);
                                }

                                #endregion
                            }
                            else
                            {
                                logger.Debug("RTPSink Listen SyncSource=" + rtpPacket.Header.SyncSource + ".");
                            }

                            previousTimestamp = timestamp;
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Received data was not a valid RTP packet. " + excp.Message);
                        }

                        #region Switching endpoint if required to cope with NAT.

                        // If a packet is recieved from an endpoint that wasn't expected treat the stream as being NATted and switch the endpoint to the socket on the NAT server. 
                        try
                        {
                            if (m_streamEndPoint != null && m_streamEndPoint.Address != null && remoteEndPoint != null && remoteEndPoint.Address != null && (m_streamEndPoint.Address.ToString() != remoteEndPoint.Address.ToString() || m_streamEndPoint.Port != remoteEndPoint.Port))
                            {
                                logger.Debug("Expecting RTP on " + IPSocket.GetSocketString(m_streamEndPoint) + " but received on " + IPSocket.GetSocketString(remoteEndPoint) + ", now sending to " + IPSocket.GetSocketString(remoteEndPoint) + ".");
                                m_streamEndPoint = remoteEndPoint;

                                if (RemoteEndPointChanged != null)
                                {
                                    try
                                    {
                                        RemoteEndPointChanged(m_streamId, remoteEndPoint);
                                    }
                                    catch (Exception changeExcp)
                                    {
                                        logger.Error("Exception RTPListener Changing Remote EndPoint. " + changeExcp.Message);
                                    }
                                }
                            }
                        }
                        catch (Exception setSendExcp)
                        {
                            logger.Error("Exception RTPListener setting SendTo Socket. " + setSendExcp.Message);
                        }

                        #endregion
                    }
                    else if (!StopListening) // Empty packet was received possibly indicating connection closure so check for timeout.
                    {
                        double noRTPRcvdDuration = (m_lastRTPReceivedTime != DateTime.MinValue) ? DateTime.Now.Subtract(m_lastRTPReceivedTime).TotalSeconds : 0;
                        double noRTPSentDuration = (m_lastRTPSentTime != DateTime.MinValue) ? DateTime.Now.Subtract(m_lastRTPSentTime).TotalSeconds : 0;

                        //logger.Warn("Remote socket closed on receive on " + m_localEndPoint.Address.ToString() + ":" + + m_localEndPoint.Port + ", reinitialising. No rtp for " + noRTPRcvdDuration + "s. last rtp " + m_lastRTPReceivedTime.ToString("dd MMM yyyy HH:mm:ss") + ".");

                        // If this check is not done then the stream will never time out if it doesn't receive the first packet.
                        if (m_lastRTPReceivedTime == DateTime.MinValue)
                        {
                            m_lastRTPReceivedTime = DateTime.Now;
                        }

                        remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                        if ((noRTPRcvdDuration > NO_RTP_TIMEOUT || noRTPSentDuration > NO_RTP_TIMEOUT) && StopIfNoData)
                        {
                            logger.Warn("Disconnecting RTP listener on " + m_localEndPoint.ToString() + " due to not being able to send or receive any RTP for " + NO_RTP_TIMEOUT + "s.");
                            Shutdown();
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Listen RTPSink: " + excp.Message);
            }
            finally
            {
                #region Shut down socket.

                Shutdown();

                if (ListenerClosed != null)
                {
                    try
                    {
                        ListenerClosed(m_streamId, m_callDescriptorId);
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception RTPSink ListenerClosed. " + excp.Message);
                    }
                }

                #endregion
            }
		}

        private void m_rtcpSampler_RTCPReportReady(RTCPReport rtcpReport)
        {
            if (rtcpReport != null && RTCPReportReady != null)
            {
                try
                { 
                    RTCPReportReady(rtcpReport);
                    rtcpReport.LastReceivedReportNumber = LastReceivedReportNumber;

                    //if (rtcpReport.ReportNumber % 3 != 0)
                    //{
                    SendRTCPReport(RTCPReportTypesEnum.RTCP, rtcpReport.GetBytes());
                    //}
                }
                catch (Exception excp)
                {
                    logger.Error("Exception m_rtcpSampler_RTCPReportReady. " + excp.Message);
                }
            }
        }

        /// <summary>
        /// Sends an RTCP report to the remote agent.
        /// </summary>
        /// <param name="rtcpReport"></param>
        public void SendRTCPReport(RTCPReportTypesEnum reportType, byte[] reportData)
        {
            try
            {
                RTCPPacket rtcpPacket = new RTCPPacket(0, 0, 0, 0, 0);

                RTCPReportPacket rtcpReportPacket = new RTCPReportPacket(reportType, reportData);
                byte[] rtcpReportPacketBytes = rtcpReportPacket.GetBytes();
                byte[] rtcpReportBytes = rtcpPacket.GetBytes(rtcpReportPacketBytes);

                m_udpListener.Send(rtcpReportBytes, rtcpReportBytes.Length, m_streamEndPoint);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendRTCPReport. " + excp.Message);
            }
        }

		private void ListenerTimeout()
		{
			try
			{
				logger.Debug("Listener timeout thread started for RTP stream " + m_streamId);
				
				// Wait for the first RTP packet to be received.
				m_lastPacketReceived.WaitOne();

				// Once we've got one packet only allow a maximum of NO_RTP_TIMEOUT between packets before shutting the stream down.
				while(!StopListening)
				{
					if(!m_lastPacketReceived.WaitOne(NO_RTP_TIMEOUT*1000, false))
					{
						logger.Debug("RTP Listener did not receive any packets for " + NO_RTP_TIMEOUT + "s, shutting down stream.");
						Shutdown();
						break;
					}

					// Shutdown the socket even if there is still RTP but the stay alive limit has been exceeded.
					if(RTPMaxStayAlive > 0 && DateTime.Now.Subtract(m_startRTPSendTime).TotalSeconds > RTPMaxStayAlive)
					{
						logger.Warn("Shutting down RTPSink due to passing RTPMaxStayAlive time.");
						Shutdown();
                        break;
					}

					m_lastPacketReceived.Reset();
				}
			}
			catch(Exception excp)
			{
				logger.Error("Exception ListenerTimeout. " + excp.Message);
				throw excp;
			}
		}

		public void StartSending(IPEndPoint serverEndPoint)
		{
			m_streamEndPoint = serverEndPoint;
			
			Thread rtpSenderThread = new Thread(new ThreadStart(Send));
			rtpSenderThread.Start();
		}

		private void Send()
		{
            try
            {
                int payloadSize = RTPPacketSendSize;
                RTPPacket rtpPacket = new RTPPacket(RTPPacketSendSize);
                byte[] rtpBytes = rtpPacket.GetBytes();

                RTPHeader rtpHeader = new RTPHeader();
                rtpHeader.SequenceNumber = (UInt16)65000;  //Convert.ToUInt16(Crypto.GetRandomInt(0, UInt16.MaxValue));
                uint sendTimestamp = uint.MaxValue - 5000;
                uint lastSendTimestamp = sendTimestamp;
                UInt16 lastSeqNum = 0;

                logger.Debug("RTP send stream starting to " + IPSocket.GetSocketString(m_streamEndPoint) + " with payload size " + payloadSize + " bytes.");

                Sending = true;
                m_startRTPSendTime = DateTime.MinValue;
                m_lastRTPSentTime = DateTime.MinValue;
                m_sampleStartSeqNo = rtpHeader.SequenceNumber;

                DateTime lastRTPSendAttempt = DateTime.Now;

                while (m_udpListener != null && !StopListening)
                {
                    // This may be changed by the listener so it needs to be set each iteration.
                    IPEndPoint dstEndPoint = m_streamEndPoint;

                    //logger.Info("Sending RTP packet to " + dstEndPoint.Address + ":"  + dstEndPoint.Port);

                    if (payloadSize != m_rtpPacketSendSize)
                    {
                        payloadSize = m_rtpPacketSendSize;
                        logger.Info("Changing RTP payload to " + payloadSize);
                        rtpPacket = new RTPPacket(RTP_HEADER_SIZE + m_rtpPacketSendSize);
                        rtpBytes = rtpPacket.GetBytes();
                    }

                    try
                    {
                        if (m_startRTPSendTime == DateTime.MinValue)
                        {
                            m_startRTPSendTime = DateTime.Now;
                            rtpHeader.MarkerBit = 0;

                            logger.Debug("RTPSink Send SyncSource=" + rtpPacket.Header.SyncSource + ".");
                        }
                        else
                        {
                            lastSendTimestamp = sendTimestamp;
                            double milliSinceLastSend = DateTime.Now.Subtract(m_lastRTPSentTime).TotalMilliseconds;
                            sendTimestamp = Convert.ToUInt32((lastSendTimestamp + (milliSinceLastSend * TIMESTAMP_FACTOR)) % uint.MaxValue);

                            if (lastSendTimestamp > sendTimestamp)
                            {
                                logger.Error("RTP Sender previous timestamp (" + lastSendTimestamp + ") > timestamp (" + sendTimestamp + ") ms since last send=" + milliSinceLastSend  + ", lastseqnum=" + lastSeqNum + ", seqnum=" + rtpHeader.SequenceNumber + ".");
                            }

                            if (DateTime.Now.Subtract(m_lastRTPSentTime).TotalMilliseconds > 75)
                            {
                                logger.Debug("delayed send: " + rtpHeader.SequenceNumber + ", time since last send " + DateTime.Now.Subtract(m_lastRTPSentTime).TotalMilliseconds + "ms.");
                            }
                        }

                        rtpHeader.Timestamp = sendTimestamp;
                        byte[] rtpHeaderBytes = rtpHeader.GetBytes();
                        Array.Copy(rtpHeaderBytes, 0, rtpBytes, 0, rtpHeaderBytes.Length);

                        // Send RTP packets and any extra channels required to emulate mutliple calls.
                        for (int channelCount = 0; channelCount < m_channels; channelCount++)
                        {
                            //logger.Debug("Send rtp getting wallclock timestamp for " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff"));
                            //DateTime sendTime = DateTime.Now;
                            //rtpHeader.Timestamp = RTPHeader.GetWallclockUTCStamp(sendTime);
                            //logger.Debug(rtpHeader.SequenceNumber + "," + rtpHeader.Timestamp);

                            m_udpListener.Send(rtpBytes, rtpBytes.Length, dstEndPoint);
                            m_lastRTPSentTime = DateTime.Now;

                            m_packetsSent++;
                            m_bytesSent += rtpBytes.Length;

                            if (m_packetsSent % 500 == 0)
                            {
                                logger.Debug("Total packets sent to " + dstEndPoint.ToString() + " " + m_packetsSent + ", bytes " + NumberFormatter.ToSIByteFormat(m_bytesSent, 2) + ".");
                            }

                            //sendLogger.Info(m_lastRTPSentTime.ToString("dd MMM yyyy HH:mm:ss:fff") + "," + m_lastRTPSentTime.Subtract(m_startRTPSendTime).TotalMilliseconds.ToString("0") + "," + rtpHeader.SequenceNumber + "," + rtpBytes.Length);

                            //sendLogger.Info(rtpHeader.SequenceNumber + "," + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff"));

                            if (DataSent != null)
                            {
                                try
                                {
                                    DataSent(m_streamId, rtpBytes, dstEndPoint);
                                }
                                catch (Exception excp)
                                {
                                    logger.Error("Exception RTPSink DataSent. " + excp.Message);
                                }
                            }

                            lastSeqNum = rtpHeader.SequenceNumber;
                            if (rtpHeader.SequenceNumber == UInt16.MaxValue)
                            {
                                //logger.Debug("RTPSink looping  the sequence number in sample.");
                                rtpHeader.SequenceNumber = 0;
                            }
                            else
                            {
                                rtpHeader.SequenceNumber++;
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception RTP Send. " + excp.GetType() + ". " + excp.Message);

                        if (excp.GetType() == typeof(SocketException))
                        {
                            logger.Error("socket exception errorcode=" + ((SocketException)excp).ErrorCode + ".");
                        }

                        logger.Warn("Remote socket closed on send. Last RTP recevied " + m_lastRTPReceivedTime.ToString("dd MMM yyyy HH:mm:ss") + ", last RTP successfull send " + m_lastRTPSentTime.ToString("dd MMM yyyy HH:mm:ss") + ".");
                    }

                    Thread.Sleep(RTPFrameSize);

                    #region Check for whether the stream has timed out on a send or receive and if so shut down the stream.

                    double noRTPRcvdDuration = (m_lastRTPReceivedTime != DateTime.MinValue) ? DateTime.Now.Subtract(m_lastRTPReceivedTime).TotalSeconds : 0;
                    double noRTPSentDuration = (m_lastRTPSentTime != DateTime.MinValue) ? DateTime.Now.Subtract(m_lastRTPSentTime).TotalSeconds : 0;
                    double testDuration = DateTime.Now.Subtract(m_startRTPSendTime).TotalSeconds;

                    if ((
                        noRTPRcvdDuration > NO_RTP_TIMEOUT || 
                        noRTPSentDuration > NO_RTP_TIMEOUT || 
                        (m_lastRTPReceivedTime == DateTime.MinValue && testDuration > NO_RTP_TIMEOUT)) // If the test request comes from a private or unreachable IP address then no RTP will ever be received. 
                        && StopIfNoData)
                    {
                        logger.Warn("Disconnecting RTP stream on " + m_localEndPoint.Address.ToString() + ":" + m_localEndPoint.Port + " due to not being able to send any RTP for " + NO_RTP_TIMEOUT + "s.");
                        StopListening = true;
                    }

                    // Shutdown the socket even if there is still RTP but the stay alive limit has been exceeded.
                    if (RTPMaxStayAlive > 0 && DateTime.Now.Subtract(m_startRTPSendTime).TotalSeconds > RTPMaxStayAlive)
                    {
                        logger.Warn("Shutting down RTPSink due to passing RTPMaxStayAlive time.");
                        Shutdown();
                        StopListening = true;
                    }

                    #endregion
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Send RTPSink: " + excp.Message);
            }
            finally
            {
                #region Shut down socket.

                Shutdown();

                if (SenderClosed != null)
                {
                    try
                    {
                        SenderClosed(m_streamId, m_callDescriptorId);
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception RTPSink SenderClosed. " + excp.Message);
                    }
                }

                #endregion
            }
		}
			
		public void Shutdown()
		{
            StopListening = true;
            
            if(!ShuttingDown)
			{
				ShuttingDown = true;

				try
				{
					m_lastPacketReceived.Set();

                    if (m_rtcpSampler != null)
                    {
                        m_rtcpSampler.Shutdown();
                    }

                    if (m_udpListener != null)
                    {
                        m_udpListener.Close();
                    }
				}
				catch(Exception excp)
				{
					logger.Warn("Exception RTPSink Shutdown (shutting down listener). "  + excp.Message);
				}	
			}
		}
    }
}
