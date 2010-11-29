//-----------------------------------------------------------------------------
// Filename: RTCP.cs
//
// Description: Implementaion of RTP Control Protocol.
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
using System.Data;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
//using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{	   
    public class RTPReceiveRecord
	{
		//public DateTime SendTime;                 // The send time of the RTP packet adjusted to be local time using a rolling avergae tranist time to calculate.
		public DateTime ReceiveTime;                // Local time the RTP packet was received.
		public UInt16 SequenceNumber;
		public long RTPBytes;			            // Includes RTP Header and payload.
        public uint Jitter;
		public int Duplicates;
        public bool InSequence = false;             // Whether the packet is the next expected one in the sequence. Only in sequnce packets are used for sampling jitter.
        public bool JitterBufferDiscard = false;    // Whether a packet would have been discarded from the jitter buffer.
        //public int AverageTransit;                  // The average tranist time in milliseconds at the time the packet was received.

        public RTPReceiveRecord(DateTime receiveTime, UInt16 sequenceNumber, long rtpBytes, uint jitter, bool inSequence, bool jitterDiscard)
		{
			//SendTime = sendTime.ToUniversalTime();
			ReceiveTime = receiveTime;
			SequenceNumber = sequenceNumber;
			RTPBytes = rtpBytes;
            Jitter = jitter;
            InSequence = inSequence;
            JitterBufferDiscard = jitterDiscard;
            //AverageTransit = averageTransit;
		}
	}

    public class RTCPReport
    {
        public const int RTCPREPORT_BYTES_LENGTH = 84;

        public Guid RTPStreamId;
        public uint ReportNumber;
        public uint LastReceivedReportNumber;       // The report number of teh last RTCP report that was received from the remote agent.
        private IPEndPoint m_remoteEndPoint;
		public string RemoteEndPoint
		{
			get{ return IPSocket.GetSocketString(m_remoteEndPoint);}
		}
		public uint SyncSource;
		public DateTime SampleStartTime;
		public DateTime SampleEndTime;
        public UInt16 StartSequenceNumber;
        public UInt16 EndSequenceNumber;
		public uint TotalPackets;
		public uint OutOfOrder;
		public uint JitterAverage;
		public uint JitterMaximum;
		public uint JitterDiscards;
		public uint PacketsLost;
		public uint Duplicates;
		//public int OutsideWindow;					// Packet received that is from a sample more than 4 x N (N = sample size) ago, it will already have been classed as dropped.
		public ulong BytesReceived;
		public uint TransmissionRate;
        public ulong Duration;
        public uint AverageTransitTime;

        // Properties to be set by classes that use the RTCPReport and are not set by the sampler.
        public Guid TestId;
        //public SIPUserAgentRoles UserAgentRole;

        public RTCPReport()
        { }

        public RTCPReport(Guid rtpStreamId, uint syncSource, IPEndPoint remoteEndPoint)
        {
            RTPStreamId = rtpStreamId;
            SyncSource = syncSource;
            m_remoteEndPoint = remoteEndPoint;
        }

        public RTCPReport(byte[] packet)
        {
            if (BitConverter.IsLittleEndian)
            {
                SyncSource = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 0));
                SampleStartTime = new DateTime((long)NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 4)));
                SampleEndTime = new DateTime((long)NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 12)));
                StartSequenceNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 20));
                EndSequenceNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 22));
                TotalPackets = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 24));
                OutOfOrder = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 28));
                JitterAverage = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 32));
                JitterMaximum = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 36));
                JitterDiscards = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 40));
                PacketsLost = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 44));
                Duplicates = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 48));
                BytesReceived = NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 52));
                TransmissionRate = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 60));
                Duration = NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 64));
                AverageTransitTime = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 72));
                ReportNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 76));
                LastReceivedReportNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 80));
            }
            else
            {
                SyncSource = BitConverter.ToUInt32(packet, 0);
                SampleStartTime = new DateTime((long)BitConverter.ToUInt64(packet, 4));
                SampleEndTime = new DateTime((long)BitConverter.ToUInt64(packet, 12));
                StartSequenceNumber = BitConverter.ToUInt16(packet, 20);
                EndSequenceNumber = BitConverter.ToUInt16(packet, 22);
                TotalPackets = BitConverter.ToUInt32(packet, 24);
                OutOfOrder = BitConverter.ToUInt32(packet, 28);
                JitterAverage = BitConverter.ToUInt32(packet, 32);
                JitterMaximum = BitConverter.ToUInt32(packet, 36);
                JitterDiscards = BitConverter.ToUInt32(packet, 40);
                PacketsLost = BitConverter.ToUInt32(packet, 44);
                Duplicates = BitConverter.ToUInt32(packet, 48);
                BytesReceived = BitConverter.ToUInt64(packet, 52);
                TransmissionRate = BitConverter.ToUInt32(packet, 60);
                Duration = BitConverter.ToUInt64(packet, 64);
                AverageTransitTime = BitConverter.ToUInt32(packet, 72);
                ReportNumber = BitConverter.ToUInt32(packet, 76);
                LastReceivedReportNumber = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 80));
            }
        }

        public RTCPReport(DataRow row)
		{
			TotalPackets = Convert.ToUInt32(row["packets"]);
			PacketsLost = Convert.ToUInt32(row["packetslost"]);
			JitterDiscards = Convert.ToUInt32(row["jitterdiscards"]);
			JitterAverage = Convert.ToUInt32(row["jitteraverage"]);
			JitterMaximum = Convert.ToUInt32(row["jittermaximum"]);
			OutOfOrder = Convert.ToUInt32(row["outoforder"]);
			Duplicates = Convert.ToUInt32(row["duplicates"]);
			SampleStartTime = Convert.ToDateTime(row["starttimestamp"]);
            SampleEndTime = Convert.ToDateTime(row["endtimestamp"]);
			BytesReceived = Convert.ToUInt32(row["bytesreceived"]);
            TransmissionRate = Convert.ToUInt32(row["transmissionrate"]);
            Duration = Convert.ToUInt64(row["duration"]);
            AverageTransitTime = Convert.ToUInt32(row["transittime"]);
		}

        public string ToResultsString()
		{		
			IPEndPoint remoteEndPoint = IPSocket.GetIPEndPoint(RemoteEndPoint);

			string results = 
				"SourceIPAddress = " + remoteEndPoint.Address + "\r\n" +
				"SourceIPPort = " + remoteEndPoint.Port + "\r\n" + 
				"StartTime = " + SampleStartTime.ToString("dd MMM yyyy HH:mm:ss:fff") + "\r\n" +
				"EndTime = " + SampleEndTime.ToString("dd MMM yyyy HH:mm:ss:fff") + "\r\n" +
				"Duration = " + SampleEndTime.Subtract(SampleStartTime).TotalSeconds.ToString("0.###") + "(s)\r\n" +
				"TotalPackets = " + TotalPackets + "\r\n" +
				"OutOfOrder = " + OutOfOrder + "\r\n" +
				//"AlreadyDropped = " + OutsideWindow + "\r\n" +
				"JitterDiscards = " + JitterDiscards + "\r\n" +
				"JitterMaximum = " + JitterMaximum + "(ms)\r\n" +
				"JitterAverage = " + JitterAverage.ToString("0.##") + "(ms)\r\n";

			return results;
		}

        public byte[] GetBytes()
        {
            byte[] payload = new byte[RTCPREPORT_BYTES_LENGTH];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SyncSource)), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((ulong)SampleStartTime.Ticks)), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((ulong)SampleEndTime.Ticks)), 0, payload, 12, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(StartSequenceNumber)), 0, payload, 20, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(EndSequenceNumber)), 0, payload, 22, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)TotalPackets)), 0, payload, 24, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)OutOfOrder)), 0, payload, 28, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)JitterAverage)), 0, payload, 32, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)JitterMaximum)), 0, payload, 36, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)JitterDiscards)), 0, payload, 40, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)PacketsLost)), 0, payload, 44, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)Duplicates)), 0, payload, 48, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((ulong)BytesReceived)), 0, payload, 52, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)TransmissionRate)), 0, payload, 60, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((ulong)Duration)), 0, payload, 64, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)AverageTransitTime)), 0, payload, 72, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)ReportNumber)), 0, payload, 76, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((uint)LastReceivedReportNumber)), 0, payload, 80, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SyncSource), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SampleStartTime.Ticks), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(SampleEndTime.Ticks), 0, payload, 12, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(StartSequenceNumber), 0, payload, 20, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(EndSequenceNumber), 0, payload, 22, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(TotalPackets), 0, payload, 24, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(OutOfOrder), 0, payload, 28, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(JitterAverage), 0, payload, 32, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(JitterMaximum), 0, payload, 36, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(JitterDiscards), 0, payload, 40, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(PacketsLost), 0, payload, 44, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(Duplicates), 0, payload, 48, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(BytesReceived), 0, payload, 52, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(TransmissionRate), 0, payload, 60, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(Duration), 0, payload, 64, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(AverageTransitTime), 0, payload, 72, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(ReportNumber), 0, payload, 76, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(LastReceivedReportNumber), 0, payload, 76, 4);
            }

            return payload;
        }
    }
	
	public class RTCPReportSampler
	{
		private const int TRANSITTIMES_QUEUE_LENGTH = 5000;
        private const int CHECK_FORSAMPLE_PERIOD = 3000;
        private const string RTCP_FORMAT_STRING = "syncsrc={0}, ts={1} ,te={2} , dur={3} ,seqs={4,-5:S} ,seqe={5,-5:S} ,pkttot={6,-3:S} ,jitmax={7,-3:S}, jitavg={8,-4:S} " + 
                                    ",transit={9,-5:S} ,pktrate={10,-5:S} ,bytestot={11,-5:S} ,bw={12,-9:S} ,drops={13} ,jitdrops={14} ,duplicates={15} ,outoforder={16}";

		private static ILog logger = log4net.LogManager.GetLogger("rtcp");

		public int JitterBufferMilliseconds = 150;	// The size of a the theoretical jitter buffer.
		public int ReportSampleDuration = 1500;		// Sample time in milliseconds after which a new sample is generated.
		//public int ReportQueueSize = 100;			// The number of samples that will be stored in the queue before they will be dropped on a FIFO basis.

        //private Queue<RTCPReport> m_samples = new Queue<RTCPReport>();	                                        // Reports.
        private Dictionary<UInt16, RTPReceiveRecord> m_rcvdSeqNums = new Dictionary<UInt16, RTPReceiveRecord>();   // [<sequence number>, <RTCPMeasurement>] used to record received sequence numbers to detect duplicates.
        private uint m_reportNumber = 1;
        private UInt16 m_windowStartSeqNum;
        private UInt16 m_windowLastSeqNum;
        private UInt16 m_windowSecondLastSeqNum;
        private DateTime m_lastSampleTime;
        //private Queue<uint> m_latestInterArrivalTimes = new Queue<uint>();

        private Guid m_rtpStreamId;
        private uint m_syncSource;
        private IPEndPoint m_remoteEndPoint;

        public event RTCPSampleReadyDelegate RTCPReportReady;

        private bool m_checkForSamples = true;
        private ManualResetEvent m_checkForSampleEvent = new ManualResetEvent(false);

		public RTCPReportSampler()
		{}

        public RTCPReportSampler(Guid rtpStreamId, uint syncSource, IPEndPoint remoteEndPoint, UInt16 startSequenceNumber, DateTime startTime, long bytesReceived)
		{
            m_rtpStreamId = rtpStreamId;
            m_syncSource = syncSource;
			m_remoteEndPoint = remoteEndPoint;

            m_windowStartSeqNum = startSequenceNumber;
            m_windowLastSeqNum = startSequenceNumber;
            m_windowSecondLastSeqNum = startSequenceNumber;

            m_lastSampleTime = DateTime.Now;

            logger.Debug("New RTCP report created for " + syncSource + " for stream from " + IPSocket.GetSocketString(remoteEndPoint) + ", start seq num=" + startSequenceNumber + ".");
			//resultsLogger.Info("StartTime,StartTimestamp,EndTime,EndTimestamp,Duration(ms),StartSeqNum,EndSeqNum,TotalPackets,TotalBytes,TransmissionRate(bps),Drops,Duplicates");

            RTPReceiveRecord measurement = new RTPReceiveRecord(startTime, startSequenceNumber, bytesReceived, 0, true, false);
            m_rcvdSeqNums.Add(startSequenceNumber, measurement);
		}

        public void StartSampling()
        {
            Thread samplingThread = new Thread(new ThreadStart(CheckForSamples));
            samplingThread.Start();
        }

        /// <summary>
        /// Records the arrival of a new RTP packet and periodically samples the mesaurements to record the characteristics of the RTP stream.
        /// </summary>
        /// <param name="sequenceNumber">RTP header sequence number, monotonically increasing in RTP stream.</param>
        /// <param name="sendTime">The remote time at which the RTP packet was sent.</param>
        /// <param name="receiveTime">The local time at which the RTP listener received the packet.</param>
        /// <param name="bytesReceived">Number of bytes received.</param>
		public void RecordRTPReceive(DateTime receiveTime, UInt16 sequenceNumber, long bytesReceived, uint jitter)
		{
			try
			{
                //logger.Debug("RecordRTPReceive " + sequenceNumber + ".");
                
                if(m_rcvdSeqNums.ContainsKey(sequenceNumber))
				{
                    logger.Debug("duplicate " + sequenceNumber + ".");
                    m_rcvdSeqNums[sequenceNumber].Duplicates = m_rcvdSeqNums[sequenceNumber].Duplicates + 1;
				}
				//else if(sequenceNumber < m_windowStartSeqNum)
				//{
                //    OutsideWindow++;
				//}
				else
				{
                    bool inSequence = (m_windowLastSeqNum != UInt16.MaxValue) ? sequenceNumber == m_windowLastSeqNum + 1 : sequenceNumber == 0;
                    //bool inSequence = (Math.Abs(sequenceNumber - m_windowLastSeqNum) > (UInt16.MaxValue / 2)) ? sequenceNumber == m_windowLastSeqNum + 1 : sequenceNumber == 0;

                    //logger.Debug(sequenceNumber + " in sequence=" + inSequence + ".");

                    /*int startJitterBufferSeq = (m_windowLastSeqNum - JitterBufferSamples >= 0) ? m_windowLastSeqNum - JitterBufferSamples : m_windowLastSeqNum  + 65535 - JitterBufferSamples;
                    int endJitterBufferSeq = (m_windowLastSeqNum + JitterBufferSamples <= 65535) ? m_windowLastSeqNum + JitterBufferSamples : m_windowLastSeqNum + JitterBufferSamples - 65535;
                    bool jitterDiscard = false;
                    
                    if(endJitterBufferSeq > startJitterBufferSeq)
                    {
                        jitterDiscard = !(sequenceNumber >= startJitterBufferSeq && sequenceNumber <= endJitterBufferSeq);
                    }
                    else
                    {
                        jitterDiscard = !(sequenceNumber >= startJitterBufferSeq || sequenceNumber <= endJitterBufferSeq);
                    }*/

                    m_windowSecondLastSeqNum = m_windowLastSeqNum;
                    m_windowLastSeqNum = sequenceNumber;

                    // Add measurement to buffer.
                    /*DateTime utcSendTime = sendTime.ToUniversalTime();
                    DateTime utcReceiveTime = receiveTime.ToUniversalTime();
                    double transitTime = (utcSendTime < utcReceiveTime) ? utcReceiveTime.Subtract(utcSendTime).TotalMilliseconds : utcSendTime.Subtract(utcReceiveTime).TotalMilliseconds;
                    */

                    //if (m_latestInterArrivalTimes.Count > TRANSITTIMES_QUEUE_LENGTH)
                    //{
                    //    m_latestInterArrivalTimes.Dequeue();
                    //}
                    //m_latestInterArrivalTimes.Enqueue(interArrivalMilliseconds);
                    //int avgArrivalTime = GetAverageTransitMilliseconds();

                    //logger.Debug("Avg transit time=" + avgTransitTime + "ms, " + sequenceNumber);

                    //double jitterAbs = Math.Abs(interArrivalMilliseconds - avgArrivalTime);
                    //logger.Debug("jitterAbs=" + jitterAbs + "ms, " + sequenceNumber);
                    //int jitter = 0;
                    //if (jitterAbs > 1)
                    //{
                    //    jitter = Convert.ToInt32(jitterAbs);
                    //}

                    bool jitterDiscard = false;
                    if (jitter >= JitterBufferMilliseconds)
                    {
                        logger.Debug("jitter discard " + sequenceNumber + ".");
                        jitterDiscard = true;
                    }

                    //logger.Debug("jitter=" + jitter + "ms, avg transit=" + avgArrivalTime);

                    //resultsLogger.Info(sequenceNumber + "," + bytesReceived + "," + avgTransitTime + "," + jitter + "," + inSequence + "," + jitterDiscard + ",[" + startJitterBufferSeq + "<=" + sequenceNumber + "<=" + endJitterBufferSeq + "]"); 

                    //RTPReceiveRecord measurement = new RTPReceiveRecord(localSendTime, utcReceiveTime, sequenceNumber, bytesReceived);
                    RTPReceiveRecord measurement = new RTPReceiveRecord(receiveTime, sequenceNumber, bytesReceived, jitter, inSequence, jitterDiscard);

                    //logger.Debug("adding measurement for " + measurement.SequenceNumber + ".");
                    lock (m_rcvdSeqNums)
                    {
                        if (m_rcvdSeqNums.ContainsKey(sequenceNumber))
                        {
                            logger.Warn("RecordRTPReceive having to remove measurement for " + sequenceNumber + " in order to accomodate new RTP measurement.");
                            m_rcvdSeqNums.Remove(sequenceNumber);
                        }

                        m_rcvdSeqNums.Add(sequenceNumber, measurement);
                    }
				}

                //return CheckForAvailableSample(sequenceNumber);
			}
			catch(Exception excp)
			{
                logger.Error("Exception RecordRTPReceive for " + sequenceNumber + ". " + excp.Message);
			}
		}

        /// <summary>
        /// A sample is taken if the last RTP measurement recorded is 4x the sample time since the last last smaple was taken.
        /// The 4x is needed in order to be able to take into account late arriving packets as out of order rather then as drops.
        /// </summary>
        /// <param name="sequenceNumber"></param>
        private RTCPReport CheckForAvailableSample(UInt16 sequenceNumber)
        {
            try
            {
                //logger.Debug("Check for available sample " + sequenceNumber + ".");
                RTCPReport sample = null;

                RTPReceiveRecord measurement = m_rcvdSeqNums[sequenceNumber];

                //logger.Debug("window start seq num=" + m_windowStartSeqNum);
                RTPReceiveRecord startSampleMeasuerment = m_rcvdSeqNums[m_windowStartSeqNum];
                
                UInt16 endSampleSeqNum = 0;
                UInt16 endSampleSeqNumMinusOne = 0;
                int samplesAvailable = 0;
                DateTime sampleCutOffTime;

                bool sampleAvailable = false;
                
                // Determine whether a sample of the RTP stream measurements should be taken.
                if (DateTime.Now.Subtract(m_lastSampleTime).TotalMilliseconds > (4 * ReportSampleDuration))
                {
                    // Make the sample a random size between N and 2N
                    int randomElement = Crypto.GetRandomInt(ReportSampleDuration, 2 * ReportSampleDuration);
                    int sampleDuration = ReportSampleDuration + randomElement;
                    sampleCutOffTime = m_lastSampleTime.AddMilliseconds(sampleDuration);

                    //logger.Debug("Sample duration=" + sampleDuration + "ms, cut off time=" + sampleCutOffTime.ToString("HH:mm:ss:fff") + ".");

                    // Get the list of RTP measurements from last time a sample was taken up to the last receive within the window.
                    int endSeqNum = (sequenceNumber < m_windowStartSeqNum) ? sequenceNumber + UInt16.MaxValue + 1: sequenceNumber;
                    //logger.Debug("Checking for sample from " + m_windowStartSeqNum + " to " + endSeqNum + ".");
                    for (int seqNum = m_windowStartSeqNum; seqNum <= endSeqNum; seqNum++)
					{
                        UInt16 testSeqNum = (seqNum > UInt16.MaxValue) ? Convert.ToUInt16((seqNum % UInt16.MaxValue) - 1) : Convert.ToUInt16(seqNum);

                        if (m_rcvdSeqNums.ContainsKey(testSeqNum))
						{
                            //logger.Debug(testSeqNum + " " + m_rcvdSeqNums[testSeqNum].ReceiveTime.ToString("ss:fff") + "<" + sampleCutOffTime.ToString("ss:fff") + ".");
                            
                            if (m_rcvdSeqNums[testSeqNum].ReceiveTime < sampleCutOffTime)
							{
                                samplesAvailable++;
                                endSampleSeqNum = testSeqNum;
                            }
                            else
                            {
                                endSampleSeqNumMinusOne = endSampleSeqNum;
                                endSampleSeqNum = testSeqNum;
                                sampleAvailable = true;
                                break;
                            }
                        }
                    }
                }
                
                /*if (m_rcvdSeqNums.Count > 200)
                {
                    endSampleSeqNum = m_windowSecondLastSeqNum;
                    sampleAvailable = true;
                }*/

                if (sampleAvailable)
                {
                    //logger.Debug(samplesAvailable + " ready for RTCP sampling, start seq num=" + m_windowStartSeqNum + " to " + endSampleSeqNumMinusOne + ".");

                    TimeSpan measurementsSampleDuration = m_rcvdSeqNums[endSampleSeqNumMinusOne].ReceiveTime.Subtract(m_rcvdSeqNums[m_windowStartSeqNum].ReceiveTime);
                    //logger.Debug("Sample available start seq num=" + m_windowStartSeqNum + " end seq num=" + endSampleSeqNumMinusOne + ", " + measurementsSampleDuration.TotalMilliseconds.ToString("0") + ".");

                    sample = Sample(m_windowStartSeqNum, endSampleSeqNumMinusOne, measurementsSampleDuration);

                    m_windowStartSeqNum = endSampleSeqNum;
                    m_lastSampleTime = m_rcvdSeqNums[endSampleSeqNum].ReceiveTime;
                }

                return sample;
            }
            catch (Exception excp)
            {
                logger.Error("Exception CheckForAvailableSample. " + excp.Message);
                return null;
            }
        }

		/// <summary>
		/// All times passed into this method should already be UTC.
		/// </summary>
        private RTCPReport Sample(UInt16 sampleStartSequenceNumber, UInt16 sampleEndSequenceNumber, TimeSpan sampleDuration)
		{
            try
            {
                RTCPReport sample = new RTCPReport(m_rtpStreamId, m_syncSource, m_remoteEndPoint);
                sample.ReportNumber = m_reportNumber;
                sample.SampleStartTime = DateTime.MinValue;
                //sample.SampleEndTime = DateTime.MinValue;
                sample.StartSequenceNumber = sampleStartSequenceNumber;
                sample.Duration = Convert.ToUInt64(sampleDuration.TotalMilliseconds);

                double jitterTotal = 0;
                //double transitTotal = 0;

                int endSequence = (sampleEndSequenceNumber < sampleStartSequenceNumber) ? sampleEndSequenceNumber + UInt16.MaxValue + 1 : sampleEndSequenceNumber;

                // logger.Debug("Sampling range " + sampleStartSequenceNumber + " to " + endSequence);

                for (int index = sampleStartSequenceNumber; index <= endSequence; index++)
                {
                    UInt16 testSeqNum = (index > UInt16.MaxValue) ? Convert.ToUInt16((index % UInt16.MaxValue) - 1) : Convert.ToUInt16(index);
                    //logger.Debug("Sampling " + testSeqNum + ".");

                    if (m_rcvdSeqNums.ContainsKey(testSeqNum))
                    {
                        RTPReceiveRecord measurement = m_rcvdSeqNums[testSeqNum];

                        //sample.SampleEndTime = measurement.SendTime;

                        if (sample.SampleStartTime == DateTime.MinValue)
                        {
                            sample.SampleStartTime = measurement.ReceiveTime;
                        }

                        sample.SampleEndTime = measurement.ReceiveTime;
                        sample.EndSequenceNumber = measurement.SequenceNumber;
                        sample.TotalPackets++;
                        sample.BytesReceived += (uint)measurement.RTPBytes;

                        if (measurement.Duplicates > 0)
                        {
                            logger.Debug("Duplicates for " + testSeqNum + " number " + measurement.Duplicates);
                            sample.Duplicates += (uint)measurement.Duplicates;
                        }

                        if (!measurement.InSequence)
                        {
                            logger.Debug("OutOfOrder for " + testSeqNum);
                            sample.OutOfOrder++;
                        }
                        else
                        {
                            // It is possible for the jitter to be negative as an average measurement is being used for the transit time and
                            // some transits could be slightly less than the average.
                            //double jitter = Math.Abs(measurement.ReceiveTime.Subtract(measurement.SendTime).TotalMilliseconds - averageTransitTime);

                            jitterTotal += measurement.Jitter;
                            //transitTotal += measurement.AverageTransit;

                            if (measurement.Jitter > sample.JitterMaximum)
                            {
                                //logger.Debug("Jitter max set to " + measurement.Jitter + " for sequence number " + measurement.SequenceNumber);
                                sample.JitterMaximum = (uint)measurement.Jitter;
                            }
                        }

                        if (measurement.JitterBufferDiscard)
                        {
                            logger.Debug("Jitter discard for " + testSeqNum);
                            sample.JitterDiscards++;
                        }

                        // Remove the measurement from the buffer.
                        // Remove the RTP measurements now that they have been sampled.
                        lock (m_rcvdSeqNums)
                        {
                            //logger.Debug("Removing " + index);
                            m_rcvdSeqNums.Remove(testSeqNum);
                        }
                    }
                    else
                    {
                        logger.Debug("Packet drop for " + index);
                        sample.PacketsLost++;
                    }
                }

                // Calculate the average jitter.
                if (sample.TotalPackets > 0)
                {
                    sample.JitterAverage = Convert.ToUInt32(jitterTotal / sample.TotalPackets);
                }

                // Calculate the average transit.
                //if (sample.TotalPackets > 0)
                //{
                //    sample.AverageTransitTime = Convert.ToUInt32(transitTotal / sample.TotalPackets);
                //}

                // Calculate the transmission rate.
                double packetRate = 0;
                if (sampleDuration.TotalMilliseconds > 0)
                {
                    sample.TransmissionRate = Convert.ToUInt32(sample.BytesReceived * 8 / sampleDuration.TotalSeconds);
                    packetRate = sample.TotalPackets / sampleDuration.TotalSeconds;
                }

                string rtcpReport = String.Format(RTCP_FORMAT_STRING, new object[]{
                    sample.SyncSource,
                    sample.SampleStartTime.ToString("HH:mm:ss:fff"), 
                    sample.SampleEndTime.ToString("HH:mm:ss:fff"), 
                    sampleDuration.TotalMilliseconds.ToString("0"),
                    sample.StartSequenceNumber.ToString(),
                    sample.EndSequenceNumber.ToString(),
                    sample.TotalPackets.ToString(),
                    sample.JitterMaximum.ToString("0"),
                    sample.JitterAverage.ToString("0.##"),
                    sample.AverageTransitTime.ToString("0.##"),
                    packetRate.ToString("0.##"),
                    sample.BytesReceived.ToString(),  
                    sample.TransmissionRate.ToString("0.##"),  
                    sample.PacketsLost.ToString(),
                    sample.JitterDiscards.ToString(),
                    sample.Duplicates.ToString(),
                    sample.OutOfOrder.ToString()});

                logger.Info(rtcpReport);

                //logger.Info("start=" + sample.SampleStartTime.ToString("HH:mm:ss:fff") + ",end=" + sample.SampleEndTime.ToString("HH:mm:ss:fff") + ",dur=" + sampleDuration.TotalMilliseconds.ToString("0") + "ms" +
                //    ",seqnnumstart=" + sample.StartSequenceNumber + ",seqnumend=" + sample.EndSequenceNumber + ",pktstotal=" + sample.TotalPackets + "p,pktrate=" + packetRate.ToString("0.##") + "pps,bytestotal=" + sample.BytesReceived + "B,bw=" + sample.TransmissionRate.ToString("0.##") + "Kbps,jitteravg=" +
                //    sample.JitterAverage.ToString("0.##") + ",jittermax=" + sample.JitterMaximum.ToString("0.##") + ",pktslost=" + sample.PacketsLost + ",jitterdiscards=" + sample.JitterDiscards + ",duplicates=" + sample.Duplicates + ",outoforder=" + sample.OutOfOrder);

                return sample;
            }
            catch (Exception excp)
            {
                logger.Error("Exception Sample. " + excp.Message);
                return null;
            }
            finally
            {
                m_reportNumber++;
            }
        }

        private void CheckForSamples()
        {
            try
            {
                Thread.Sleep(CHECK_FORSAMPLE_PERIOD);   // Wait until the first sample is likely to be ready.
                
                while (m_checkForSamples)
                {
                    RTCPReport report = CheckForAvailableSample(m_windowLastSeqNum);

                    if (report != null && RTCPReportReady != null)
                    {
                        try
                        {
                            RTCPReportReady(report);
                        }
                        catch { }
                    }

                    m_checkForSampleEvent.Reset();
                    m_checkForSampleEvent.WaitOne(CHECK_FORSAMPLE_PERIOD, false);
                }
            }
            catch (Exception excp)
            {
                logger.Debug("Exception CheckForSamples. " + excp.Message);
            }
        }

        public void Shutdown()
        {
            try
            {
                logger.Debug("Shutting down RTCPReportSampler for syncsource= " + m_syncSource + " on stream from " + m_remoteEndPoint.ToString () + ".");

                m_checkForSamples = false;
                m_checkForSampleEvent.Set();
            }
            catch(Exception excp)
            {
                logger.Error("Exception RTCPReportSampler Shutdown. " + excp.Message);
            }
        }

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RTCPReportUnitTest
		{	
			[TestFixtureSetUp]
			public void Init()
			{ }
			
			[TestFixtureTearDown]
			public void Dispose()
			{ }

            [Test]
            public void SampleTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            }

			/*			
			[Test]
			public void InitialSampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				ushort syncSource = 1234;
				IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
				ushort startSeqNum = 1;

				RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);

				report.AddSample(startSeqNum++, DateTime.Now, 100);

				Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

				Assert.IsTrue(report.TotalPackets == 1, "Incorrect number of packets in report.");
			}


			[Test]
			public void EmpytySampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				ushort syncSource = 1234;
				IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
				ushort startSeqNum = 1;

				RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);
				report.ReportSampleDuration = 100; // Reduce report duration for unit test.

				report.AddSample(startSeqNum++, DateTime.Now, 100);
				
				Thread.Sleep(50);

				report.AddSample(startSeqNum++, DateTime.Now, 100);

				Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

				Thread.Sleep(300);

				report.AddSample(startSeqNum++, DateTime.Now, 100);

				Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

				Assert.IsTrue(report.m_samples.Count == 2, "Incorrect number of reports in the queue.");

				RTCPReport sample1 = report.GetNextSample();
				RTCPReport sample2 = report.GetNextSample();

				Console.WriteLine("Sample1: " + sample1.SampleStartTime.ToString("mm:ss:fff") + " to " + sample1.SampleEndTime.ToString("mm:ss:fff"));
				Console.WriteLine("Sample2: " + sample2.SampleStartTime.ToString("mm:ss:fff") + " to " + sample2.SampleEndTime.ToString("mm:ss:fff"));

				Assert.IsTrue(sample1.TotalPackets == 2, "Incorrect number of packets in sample1.");
				Assert.IsTrue(report.m_previousSample == null, "Previous sample should have been null after an empty sample.");

				//Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

				report.AddSample(startSeqNum++, DateTime.Now, 100);
				report.AddSample(startSeqNum++, DateTime.Now, 100);
				report.AddSample(startSeqNum++, DateTime.Now, 100);

				Thread.Sleep(120);

				Console.WriteLine("new sample");

				report.AddSample(startSeqNum++, DateTime.Now, 100);
				report.AddSample(startSeqNum++, DateTime.Now, 100);

				Thread.Sleep(120);

				Console.WriteLine("new sample");

				report.AddSample(startSeqNum++, DateTime.Now, 100);

				Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

				sample1 = report.GetNextSample();

				Console.WriteLine("Sample1: " + sample1.SampleStartTime.ToString("mm:ss:fff") + " to " + sample1.SampleEndTime.ToString("mm:ss:fff"));
				Console.WriteLine(sample1.StartSequenceNumber + " to " + sample1.EndSequenceNumber);
			}


			[Test]
			public void DropTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				ushort syncSource = 1234;
				IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
				ushort seqNum = 1;

				RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);
				report.ReportSampleDuration = 100;	

				report.AddSample(seqNum, DateTime.Now, 100);
				
				seqNum += 2;
				
				report.AddSample(seqNum, DateTime.Now, 100);

				Console.WriteLine("total packets = " + report.TotalPackets + ", outoforder = " + report.OutOfOrderPackets + ", drop " + report.PacketsLost + "."); 

				Assert.IsTrue(report.TotalPackets == 2, "Incorrect packet count in sample.");
				Assert.IsTrue(report.PacketsLost == 1, "Incorrect dropped packet count.");	

				Thread.Sleep(120);

				report.AddSample(seqNum++, DateTime.Now, 100);

				Thread.Sleep(120);

				report.AddSample(seqNum++, DateTime.Now, 100);

				Assert.IsTrue(report.m_samples.Count == 1, "Queue size was incorrect.");

				RTCPReport sample1 = report.GetNextSample();
				Console.WriteLine("Packets lost = " + sample1.PacketsLost);

				Assert.IsTrue(sample1.PacketsLost == 1, "Packets lost count was incorrect.");
			}

			[Test]
			public void OutOfOrderTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

				ushort syncSource = 1234;
				IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
				ushort seqNum = 1;

				RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);
				report.ReportSampleDuration = 100000;	// Stop timings interfering.

				report.AddSample(seqNum, DateTime.Now, 100);
				
				seqNum += 2;
				
				report.AddSample(seqNum, DateTime.Now, 100);
				report.AddSample(Convert.ToUInt16(seqNum-1), DateTime.Now, 100);

				Console.WriteLine("total packets = " + report.TotalPackets + ", outoforder = " + report.OutOfOrderPackets + "."); 

				Assert.IsTrue(report.TotalPackets == 3, "Incorrect packet count in sample.");
				Assert.IsTrue(report.OutOfOrderPackets == 2, "Incorrect outoforder packet count.");	
			}
			*/
		}

		#endif

		#endregion
	}	
}
