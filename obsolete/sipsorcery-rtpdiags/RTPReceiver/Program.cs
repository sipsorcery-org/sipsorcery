using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using NAudio;
using NAudio.Codecs;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Impl;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client.MessagePatterns;
using log4net;

namespace RTPReceiver
{
    public class RTPDiagnosticsJob
    {
        private const int RTP_PORTRANGE_START = 18000;
        private const int RTP_PORTRANGE_END = 20000;

        private static ILog logger = AppState.logger;

        private static ArrayList m_inUsePorts = new ArrayList();
        
        private SIPRequest m_request;
        private SDP m_remoteSDP;
        private RTPChannel m_rtpChannel;
        //private MemoryStream m_outStream = new MemoryStream();
        //private WaveStream m_outPCMStream;
        private WaveFileWriter m_waveFileWriter;
        //private RawSourceWaveStream m_rawSourceStream;
        //private StreamWriter m_rawRTPPayloadWriter;
        //private StreamReader m_rawRTPPayloadReader;

        public SIPServerUserAgent UAS;
        public SDP LocalSDP;
        public IPEndPoint RTPListenEndPoint;
        //public UdpClient RTPSocket;
        public IPEndPoint RemoteRTPEndPoint;
        public bool StopJob;
        public bool RTPPacketReceived;
        public bool ErrorOnRTPSend;

        public string QueueName
        {
            get { return m_request.URI.User;  }
        }

        /// <param name="rtpListenAddress">The local IP address to establish the RTP listener socket on.</param>
        /// <param name="sdpAdvertiseAddress">The public IP address to put into the SDP sent back to the caller.</param>
        /// <param name="request">The INVITE request that instigated the RTP diagnostics job.</param>
        public RTPDiagnosticsJob(IPAddress rtpListenAddress, IPAddress sdpAdvertiseAddress, SIPServerUserAgent uas, SIPRequest request)
        {
            m_request = request;
            m_remoteSDP = SDP.ParseSDPDescription(request.Body);
            RemoteRTPEndPoint = new IPEndPoint(IPAddress.Parse(m_remoteSDP.Connection.ConnectionAddress), m_remoteSDP.Media[0].Port);
            UAS = uas;
            //m_rawSourceStream = new RawSourceWaveStream(m_outStream, WaveFormat.CreateMuLawFormat(8000, 1));
            //m_waveFileWriter = new WaveFileWriter("out.wav", new WaveFormat(8000, 16, 1));
            m_waveFileWriter = new WaveFileWriter("out.wav", new WaveFormat(8000, 16, 1));
            //m_outPCMStream = WaveFormatConversionStream.CreatePcmStream(m_rawSourceStream);
            //m_rawRTPPayloadWriter = new StreamWriter("out.rtp");
            //m_rawRTPPayloadReader = new StreamReader("in.rtp");
            //IPEndPoint rtpListenEndPoint = null;
            IPEndPoint rtpListenEndPoint = null;
            NetServices.CreateRandomUDPListener(rtpListenAddress, RTP_PORTRANGE_START, RTP_PORTRANGE_END, m_inUsePorts, out rtpListenEndPoint);
            RTPListenEndPoint = rtpListenEndPoint;
            m_inUsePorts.Add(rtpListenEndPoint.Port);
            //RTPListenEndPoint = new IPEndPoint(rtpListenAddress, RTP_PORTRANGE_START);
            m_rtpChannel = new RTPChannel(RTPListenEndPoint);
            m_rtpChannel.SampleReceived += SampleReceived;
            ThreadPool.QueueUserWorkItem(delegate { GetAudioSamples(); });
            
            LocalSDP = new SDP()
            {
                SessionId = Crypto.GetRandomString(6),
                Address = sdpAdvertiseAddress.ToString(),
                SessionName = "sipsorcery",
                Timing = "0 0",
                Connection = new SDPConnectionInformation(sdpAdvertiseAddress.ToString()),
                Media = new List<SDPMediaAnnouncement>() 
                {
                    new SDPMediaAnnouncement()
                    {
                        Media = SDPMediaTypesEnum.audio,
                        Port = RTPListenEndPoint.Port,
                        MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU) }
                    }
                }
            };
        }

        public void Stop()
        {
            StopJob = true;
            m_inUsePorts.Remove(RTPListenEndPoint.Port);
            m_waveFileWriter.Close();
            m_waveFileWriter.Dispose();
            //m_rawRTPPayloadWriter.Close();
            //m_rawRTPPayloadReader.Close();
        }

        private void GetAudioSamples()
        {
            ////var pcmStream = WaveFormatConversionStream.CreatePcmStream(new Mp3FileReader("whitelight.mp3"));
            //var pcmStream = new WaveFileReader("whitelight-ulaw.wav");
            //byte[] sampleBuffer = new byte[160];
            //int bytesRead = pcmStream.Read(sampleBuffer, 0, 160);
            ////int bytesRead = m_rawRTPPayloadReader.BaseStream.Read(sampleBuffer, 0, 160);
            //while (bytesRead > 0)
            //{
            //    m_rtpChannel.AddSample(sampleBuffer);
            //    bytesRead = pcmStream.Read(sampleBuffer, 0, 160);
            //    //bytesRead = m_rawRTPPayloadReader.BaseStream.Read(sampleBuffer, 0, 160);
            //}

            var pcmFormat = new WaveFormat(8000, 16, 1);
            var ulawFormat = WaveFormat.CreateMuLawFormat(8000, 1);

            using (WaveFormatConversionStream pcmStm = new WaveFormatConversionStream(pcmFormat, new Mp3FileReader("whitelight.mp3")))
            {
                using (WaveFormatConversionStream ulawStm = new WaveFormatConversionStream(ulawFormat, pcmStm))
                {
                    byte[] buffer = new byte[160];
                    int bytesRead = ulawStm.Read(buffer, 0, 160);

                    while (bytesRead > 0)
                    {
                        byte[] sample = new byte[bytesRead];
                        Array.Copy(buffer, sample, bytesRead);
                        m_rtpChannel.Send(sample, 20);

                        bytesRead = ulawStm.Read(buffer, 0, 160);
                    }
                }
            }

            logger.Debug("Finished adding audio samples.");
        }

        private void SampleReceived(byte[] sample, int headerLength)
        {
            if (sample != null)
            {
                //using(MemoryStream sampleStream = new MemoryStream(sample))
                //{
                //    using (var rawSourceStream = new RawSourceWaveStream(sampleStream, WaveFormat.CreateMuLawFormat(8000, 1)))
                //    {
                //        using (var pcmConversionStream = WaveFormatConversionStream.CreatePcmStream(rawSourceStream))
                //        {
                //            byte[] buffer = new byte[1024];
                //            int bytesRead = pcmConversionStream.Read(buffer, 0, 1024);
                //            while (bytesRead > 0)
                //            {
                //                m_waveFileWriter.Write(buffer, 0, bytesRead);
                //                bytesRead = pcmConversionStream.Read(buffer, 0, 1024);
                //            }
                //        }
                //    }
                //}

                for (int index = headerLength; index < sample.Length; index++)
                {
                    short pcm = MuLawDecoder.MuLawToLinearSample(sample[index]);
                    m_waveFileWriter.WriteByte((byte)(pcm & 0xFF));
                    m_waveFileWriter.WriteByte((byte)(pcm >> 8));
                }
            }
        }
    }

    class Program
    {
        private const string RTP_RECEIVER_NODE = "rtpreceiver";             // The app.config node name used by this program.
        private const string SIP_SOCKETS_NODE = "sipsockets";               // The XML node within this program's config node that specifies the SIP sockets to use.
        private const string RABBITMQ_HOST_KEY = "RabbitMQHost";            // The hostname of the RabbitMQ server.
        private const string RABBITMQ_USERNAME_KEY = "RabbitMQUsername";    // The username of the RabbitMQ server.
        private const string RABBITMQ_PASSWORD_KEY = "RabbitMQPassword";    // The password of the RabbitMQ server.
        private const string RTP_LISTENIPADDRESS_KEY = "RTPListenIPAddress";// The IP address to use to listen to incoming RTP packets.
        private const string PUBLIC_IPADDRESS_KEY = "PublicIPAddress";      // The IP address to use in SIP & SDP packets.
        private const string MASTER_QUEUE_NAME = "master";
        private const int RTP_RECEIVE_TIMEOUT = 500000;                       // The maximum amount of time to wait for an RTP packet from the remote end.
        private const int HANGUP_TIMEOUT = 500000;        // The number of miliseconds after which to hangup the test call.

        private static ILog logger = AppState.logger;

        private static ICMPListener m_listener;
        private static IModel m_channel;
        private static IConnection m_connection;
        private static SIPTransport m_sipTransport;
        private static bool m_exit;
        
        private static string m_rabbitMQHost;
        private static string m_rabbitMQUsername;
        private static string m_rabbitMQPassword;
        private static IPAddress m_rtpListenIPAddress;
        private static IPAddress m_publicIPAddress;

        private static Dictionary<int, RTPDiagnosticsJob> m_rtpJobs = new Dictionary<int, RTPDiagnosticsJob>(); // [<RTP port>, <RTPDiagnosticsJob>].

        static void Main(string[] args)
        {
            try
            {
                var appNode = (XmlNode)AppState.GetSection(RTP_RECEIVER_NODE);
                if (appNode == null)
                {
                    throw new ApplicationException("The application could not be started, no " + RTP_RECEIVER_NODE + " node could be found.");
                }

                m_rabbitMQHost = appNode.SelectSingleNode(RABBITMQ_HOST_KEY).Attributes["value"].Value;
                m_rabbitMQUsername = appNode.SelectSingleNode(RABBITMQ_USERNAME_KEY).Attributes["value"].Value;
                m_rabbitMQPassword = appNode.SelectSingleNode(RABBITMQ_PASSWORD_KEY).Attributes["value"].Value;
                m_rtpListenIPAddress = IPAddress.Parse(appNode.SelectSingleNode(RTP_LISTENIPADDRESS_KEY).Attributes["value"].Value);
                m_publicIPAddress = IPAddress.Parse(appNode.SelectSingleNode(PUBLIC_IPADDRESS_KEY).Attributes["value"].Value);

                //ThreadPool.QueueUserWorkItem(delegate { InitialiseRabbitMQ(); });

                ThreadPool.QueueUserWorkItem(delegate { InitialiseSIP(appNode.SelectSingleNode(SIP_SOCKETS_NODE), m_publicIPAddress); });

                //ThreadPool.QueueUserWorkItem(delegate { InitialiseICMPListener(); });
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp.Message);
            }
            finally
            {
                Console.WriteLine("Press any key to exit.");
                Console.Read();

                m_exit = true;

                if (m_sipTransport != null)
                {
                    m_sipTransport.Shutdown();
                }

                if (m_channel != null)
                {  
                    m_channel.Close(200, "Goodbye");
                    m_connection.Close();
                }

                if (m_listener != null)
                {
                    m_listener.Stop();
                }
            }
        }

        //private static void StartRTPListener(RTPDiagnosticsJob rtpJob)
        //{
        //    logger.Debug("Starting RTP listener for queue " + rtpJob.QueueName + " on " + rtpJob.RTPListenEndPoint + ".");

        //    string queueName = rtpJob.QueueName;
        //    bool packetRecived = false;
        //    rtpJob.RTPSocket.Client.ReceiveTimeout = RTP_RECEIVE_TIMEOUT;

        //    while (!m_exit && !rtpJob.StopJob)
        //    {
        //        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        //        try
        //        {
        //            var buffer = rtpJob.RTPSocket.Receive(ref remoteEP);

        //            if (m_exit || rtpJob.StopJob)
        //            {
        //                break;
        //            }

        //            if (!packetRecived)
        //            {
        //                rtpJob.RTPPacketReceived = true;
        //                logger.Debug(buffer.Length + " bytes received on RTP socket from " + remoteEP + ".");
        //                Publish(queueName, "RTP received from " + remoteEP + ".");
        //            }
        //        }
        //        catch (Exception excp)
        //        {
        //            if (m_exit || rtpJob.StopJob)
        //            {
        //                break;
        //            }

        //            logger.Error("Exception listening for " + rtpJob.RemoteRTPEndPoint + ". " + excp.GetType() + " " + excp.Message);
        //            if (!packetRecived)
        //            {
        //                Publish(queueName, "Was not able to receive RTP from " + rtpJob.RemoteRTPEndPoint + ".");
        //            }
        //            break;
        //        }

        //        try
        //        {
        //            if (!packetRecived)
        //            {
        //                packetRecived = true;
        //                // Send a packet back to the remote RTP socket.
        //                Publish(queueName, "Sending dummy packet to " + remoteEP + ".");
        //                rtpJob.RTPSocket.Send(new byte[] { 0x00 }, 1, remoteEP);
        //            }
        //        }
        //        catch (Exception sendExcp)
        //        {
        //            if (m_exit || rtpJob.StopJob)
        //            {
        //                break;
        //            }

        //            rtpJob.ErrorOnRTPSend = true;
        //            logger.Error("Exception sending for " + remoteEP + ". " + sendExcp.GetType() + " " + sendExcp.Message);
        //            if (!packetRecived)
        //            {
        //                Publish(queueName, "Error attempting to send RTP to " + remoteEP + ".");
        //            }
        //            break;
        //        }
        //    }

        //    if (!rtpJob.StopJob)
        //    {
        //        rtpJob.Stop();
        //    }
        //}

        private static void InitialiseICMPListener()
        {
            try
            {
                m_listener = new ICMPListener(m_rtpListenIPAddress);
                m_listener.Receive +=  (icmp, ep) =>
               {
                   var rtpJob = (from job in m_rtpJobs.Values where job.RemoteRTPEndPoint.Address.ToString() == ep.Address.ToString() select job).FirstOrDefault();
                   if (rtpJob != null)
                   {
                       rtpJob.ErrorOnRTPSend = true;
                       Publish(rtpJob.QueueName, "ICMP packet received from " + ep.Address.ToString() + ".");
                   }
                };
                m_listener.Start();
            }
            catch (Exception excp)
            {
                logger.Error("Exception InitialiseRTPListener. " + excp.Message);
            }
        }

        private static void InitialiseSIP(XmlNode sipSocketsNode, IPAddress publicIPAddress)
        {
            m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine(), true);
            m_sipTransport.ContactIPAddress = publicIPAddress;
            SIPDNSManager.SIPMonitorLogEvent = LogTraceMessage;
            List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(sipSocketsNode);
            m_sipTransport.AddSIPChannel(sipChannels);

            m_sipTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

            //m_sipTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { Console.WriteLine("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
            //m_sipTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { Console.WriteLine("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
            //m_sipTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { Console.WriteLine("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
            //m_sipTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { Console.WriteLine("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
        }

        private static void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                var rtpJob = (from job in m_rtpJobs.Values where job.UAS.CallRequest.Header.CallId == sipRequest.Header.CallId select job).FirstOrDefault();

                if (rtpJob != null)
                {
                    rtpJob.Stop();
                    // Call has been hungup by remote end.
                    Console.WriteLine("Call hungup by client: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".\n");
                    Publish(rtpJob.QueueName, "BYE request received from " + remoteEndPoint + " for " + sipRequest.URI.ToString() + ".");
                    //Console.WriteLine("Request Received " + localSIPEndPoint + "<-" + remoteEndPoint + "\n" + sipRequest.ToString());
                    //m_uas.SIPDialogue.Hangup(m_sipTransport, null);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    m_sipTransport.SendResponse(okResponse);
                }
                else
                {
                    Console.WriteLine("Unmatched BYE request received for " + sipRequest.URI.ToString() + ".\n");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                Console.WriteLine("Incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".\n");
                Publish(sipRequest.URI.User, "INVITE request received from " + remoteEndPoint + " for " + sipRequest.URI.ToString() + ".");

                Console.WriteLine(sipRequest.Body);

                SIPPacketMangler.MangleSIPRequest(SIPMonitorServerTypesEnum.Unknown, sipRequest, null, LogTraceMessage);

                UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                var uas = new SIPServerUserAgent(m_sipTransport, null, null, null, SIPCallDirection.In, null, null, LogTraceMessage, uasTransaction);
                uas.CallCancelled += UASCallCancelled;

                RTPDiagnosticsJob rtpJob = new RTPDiagnosticsJob(m_rtpListenIPAddress, m_publicIPAddress, uas, sipRequest);
                
                string sdpAddress = SDP.GetSDPRTPEndPoint(sipRequest.Body).Address.ToString();

                // Only mangle if there is something to change. For example the server could be on the same private subnet in which case it can't help.
                IPEndPoint expectedRTPEndPoint = new IPEndPoint(rtpJob.RemoteRTPEndPoint.Address, rtpJob.RemoteRTPEndPoint.Port);
                if (IPSocket.IsPrivateAddress(rtpJob.RemoteRTPEndPoint.Address.ToString()))
                {
                    expectedRTPEndPoint.Address = remoteEndPoint.Address;
                }

                Publish(sipRequest.URI.User, "Advertised RTP remote socket " + rtpJob.RemoteRTPEndPoint + ", expecting from " + expectedRTPEndPoint + ".");
                m_rtpJobs.Add(rtpJob.RTPListenEndPoint.Port, rtpJob);

                //ThreadPool.QueueUserWorkItem(delegate { StartRTPListener(rtpJob); });

                Console.WriteLine(rtpJob.LocalSDP.ToString());

                uas.Answer("application/sdp", rtpJob.LocalSDP.ToString(), CallProperties.CreateNewTag(), null, SIPDialogueTransferModesEnum.NotAllowed);

                var hangupTimer = new Timer(delegate
                {
                    if (!rtpJob.StopJob)
                    {
                        if (uas != null && uas.SIPDialogue != null)
                        {
                            if(rtpJob.RTPPacketReceived && !rtpJob.ErrorOnRTPSend)
                            {
                                Publish(sipRequest.URI.User, "Test completed. There were no RTP send or receive errors.");
                            }
                            else if (!rtpJob.RTPPacketReceived)
                            {
                                Publish(sipRequest.URI.User, "Test completed. An error was identified, no RTP packets were received.");
                            }
                            else
                            {
                                Publish(sipRequest.URI.User, "Test completed. An error was identified, there was a problem when attempting to send an RTP packet.");
                            }
                            rtpJob.Stop();
                            uas.SIPDialogue.Hangup(m_sipTransport, null);
                        }
                    }
                }, null, HANGUP_TIMEOUT, Timeout.Infinite);
            }
            else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
            {
                UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                if (inviteTransaction != null)
                {
                    Console.WriteLine("Matching CANCEL request received " + sipRequest.URI.ToString() + ".\n");
                    Publish(sipRequest.URI.User, "CANCEL request received from " + remoteEndPoint + " for " + sipRequest.URI.ToString() + ".");
                    SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                    cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
                else
                {
                    Console.WriteLine("No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".\n");
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
            }
            else
            {
                Console.WriteLine("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.\n");
                Publish(sipRequest.URI.User, sipRequest.Method + " request received from " + remoteEndPoint + " for " + sipRequest.URI.ToString() + ".");
                SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                m_sipTransport.SendResponse(notAllowedResponse);
            }
        }

        private static void UASCallCancelled(ISIPServerUserAgent uas)
        {
            Console.WriteLine("Incoming call cancelled for: " + uas.CallDestination + "\n");
        }

        private static void LogTraceMessage(SIPMonitorEvent monitorEvent)
        {
            if (monitorEvent is SIPMonitorConsoleEvent)
            {
                SIPMonitorConsoleEvent consoleEvent = monitorEvent as SIPMonitorConsoleEvent;

                if (consoleEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace &&
                               consoleEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction &&
                               consoleEvent.EventType != SIPMonitorEventTypesEnum.Timing &&
                               consoleEvent.EventType != SIPMonitorEventTypesEnum.UnrecognisedMessage &&
                               consoleEvent.EventType != SIPMonitorEventTypesEnum.NATKeepAliveRelay &&
                               consoleEvent.EventType != SIPMonitorEventTypesEnum.BadSIPMessage)
                {
                    logger.Debug("Monitor: " + monitorEvent.Message);
                }
            }
        }

        private static void InitialiseRabbitMQ()
        {
            ConnectionFactory factory = new ConnectionFactory();
            factory.HostName = m_rabbitMQHost;
            factory.UserName = m_rabbitMQUsername;
            factory.Password = m_rabbitMQPassword;
            m_connection = factory.CreateConnection();
            m_channel = m_connection.CreateModel();
            m_channel.QueueDeclare(MASTER_QUEUE_NAME, true, false, false, null);
        }

        private static void Publish(string queueName, string message)
        {
            if (m_channel != null)
            {
                IBasicProperties unreliableProps = m_channel.CreateBasicProperties();
                unreliableProps.DeliveryMode = 1;
                IBasicProperties reliableProps = m_channel.CreateBasicProperties();
                reliableProps.DeliveryMode = 2;

                // Publishing to an exmpty exchange will result in RabbitMQ forwarding the message to the queue it finds with the 
                // same name as the routing key.
                m_channel.BasicPublish("", MASTER_QUEUE_NAME, reliableProps, Encoding.UTF8.GetBytes(message));
                m_channel.BasicPublish("", queueName, unreliableProps, Encoding.UTF8.GetBytes(message));
            }
        }
    }
}
