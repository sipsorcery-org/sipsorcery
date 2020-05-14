//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: This example is the same as the WebRTCTestPatternServer example
// except that it can be used without requiring a web socket signalling channel.
// 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia;

namespace WebRTCServer
{
    class Program
    {
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static SIPSorceryMedia.VpxEncoder _vpxEncoder;
        private static SIPSorceryMedia.ImageConvert _colorConverter;
        private static Bitmap _testPattern;
        private static int _stride;
        private static Timer _sendTestPatternTimer;

        private static event Action<SDPMediaTypesEnum, uint, byte[]> OnTestPatternSampleReady;

        static async Task Main()
        {
            Console.WriteLine("WebRTC No Signalling Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            // Initialise OpenSSL & libsrtp, saves a couple of seconds for the first client connection.
            Console.WriteLine("Initialising OpenSSL and libsrtp...");
            DtlsHandshake.InitialiseOpenSSL();
            Srtp.InitialiseLibSrtp();

            InitialiseTestPattern();

            Console.WriteLine($"Starting Peer Connection...");

            var pc = await StartPeerConnection();

            // Start the DTLS task before entering the ICE credentials to avoid DTLS client Hello retransmits. 
            // Start the DTLS task immediately so that we are ready to receive the client Hello.
            // If it's missed it can take 10 or more seconds for the peer to retry.
            var dtlsTask = DoDtlsHandshake(pc);

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                _sendTestPatternTimer?.Dispose();
                exitMre.Set();
            };

            Console.WriteLine("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
            Console.WriteLine("THE SDP OFFER ABOVE NEEDS TO BE PASTED INTO YOUR BROWSER");
            Console.WriteLine();

            Console.WriteLine("Press enter when you the SDP answer is available and then enter the ICE username and password...");
            Console.ReadLine();

            Console.Write("Enter the remote peer ICE User (e.g. for 'a=ice-ufrag:tGXy' enter tGXy) => ");
            var remoteIceUser = Console.ReadLine();

            Console.Write("Enter the remote peer ICE Password (e.g. for 'a=ice-pwd:Icew1/BpwUIJLn2dBMbQyYPB' enter Icew1/BpwUIJLn2dBMbQyYPB) => ");
            var remoteIcePassword = Console.ReadLine();

            pc.IceSession.SetRemoteCredentials(remoteIceUser, remoteIcePassword);

            var dtlsResult = await dtlsTask;

            logger.LogDebug($"dtls handshake result {dtlsResult}.");

            if (dtlsResult)
            {
                var remoteEP = pc.IceSession.ConnectedRemoteEndPoint;
                pc.SetDestination(SDPMediaTypesEnum.audio, remoteEP, remoteEP);
            }
            else
            {
                pc.Close("dtls handshake failed.");
            }

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();

            pc.Close("normal");
        }

        private static async Task<RTCPeerConnection> StartPeerConnection()
        {
            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                certificates = new List<RTCCertificate>
                {
                    new RTCCertificate
                    {
                        X_CertificatePath = DTLS_CERTIFICATE_PATH,
                        X_KeyPath = DTLS_KEY_PATH,
                        X_Fingerprint = DTLS_CERTIFICATE_FINGERPRINT
                    }
                }
            };

            var peerConnection = new RTCPeerConnection(pcConfiguration);

            MediaStreamTrack videoTrack = new MediaStreamTrack(
                "0", 
                SDPMediaTypesEnum.video, 
                false, 
                new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, 
                MediaStreamStatusEnum.SendOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;

            //peerConnection.OnRtcpBye += (reason) =>
            //{
            //    logger.LogInformation("RTCP BYE report received from remote peer.");
            //    peerConnection.Close(reason);
            //    dtls.Shutdown();
            //};

            peerConnection.OnTimeout += (mediaType) =>
            {
                peerConnection.Close("remote timeout");
            };

            peerConnection.onconnectionstatechange += async (state) =>
            {
                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    OnTestPatternSampleReady -= peerConnection.SendMedia;
                    peerConnection.OnReceiveReport -= RtpSession_OnReceiveReport;
                    peerConnection.OnSendReport -= RtpSession_OnSendReport;
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    // The DTLS handshake completed.
                    logger.LogDebug("Peer connection connected.");
                    OnTestPatternSampleReady += peerConnection.SendMedia;

                    await peerConnection.Start();

                    if (_sendTestPatternTimer == null)
                    {
                        _sendTestPatternTimer = new Timer(SendTestPattern, null, 0, TEST_PATTERN_SPACING_MILLISECONDS);
                    }
                }
            };

            peerConnection.oniceconnectionstatechange += (state) =>
            {
                logger.LogDebug($"ICE connection state change {state}.");

                // The ICE connectivity check completed successfully.
                if(state == RTCIceConnectionState.connected)
                {
                    var remoteEP = peerConnection.IceSession.ConnectedRemoteEndPoint;
                    peerConnection.SetDestination(SDPMediaTypesEnum.audio, remoteEP, remoteEP);
                }
            };

            var offerSdp = await peerConnection.createOffer(null);
            await peerConnection.setLocalDescription(offerSdp);

            logger.LogDebug(offerSdp.sdp);

            return peerConnection;
        }

        /// <summary>
        /// Hands the socket handle to the DTLS context and waits for the handshake to complete.
        /// </summary>
        /// <param name="webRtcSession">The WebRTC session to perform the DTLS handshake on.</param>
        private static async Task<bool> DoDtlsHandshake(RTCPeerConnection peerConnection)
        {
            logger.LogDebug("DoDtlsHandshake started.");

            if (!File.Exists(DTLS_CERTIFICATE_PATH))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {DTLS_CERTIFICATE_PATH}.");
            }
            else if (!File.Exists(DTLS_KEY_PATH))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {DTLS_KEY_PATH}.");
            }

            var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);

            var dtlsResult = await Task.Run(() => dtls.DoHandshakeAsServer((ulong)peerConnection.GetRtpChannel(SDPMediaTypesEnum.audio).RtpSocket.Handle));

            logger.LogDebug("DtlsContext initialisation result=" + dtlsResult);

            if (dtls.IsHandshakeComplete())
            {
                logger.LogDebug("DTLS handshake succeeded.");

                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                peerConnection.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                return true;
            }
            else
            {
                logger.LogWarning("DTLS handshake failed.");

                dtls.Shutdown();
                return false;
            }
        }

        private static void InitialiseTestPattern()
        {
            _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);

            // Get the stride.
            Rectangle rect = new Rectangle(0, 0, _testPattern.Width, _testPattern.Height);
            System.Drawing.Imaging.BitmapData bmpData =
                _testPattern.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                _testPattern.PixelFormat);

            // Get the address of the first line.
            _stride = bmpData.Stride;

            _testPattern.UnlockBits(bmpData);

            // Initialise the video codec and color converter.
            _vpxEncoder = new VpxEncoder();
            _vpxEncoder.InitEncoder((uint)_testPattern.Width, (uint)_testPattern.Height, (uint)_stride);

            _colorConverter = new ImageConvert();
        }

        private static void SendTestPattern(object state)
        {
            try
            {
                lock (_sendTestPatternTimer)
                {
                    unsafe
                    {
                        byte[] sampleBuffer = null;
                        byte[] encodedBuffer = null;

                        if (OnTestPatternSampleReady != null)
                        {
                            var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                            sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                            fixed (byte* p = sampleBuffer)
                            {
                                byte[] convertedFrame = null;
                                _colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.BGR24, _testPattern.Width, _testPattern.Height, _stride, VideoSubTypesEnum.I420, ref convertedFrame);

                                fixed (byte* q = convertedFrame)
                                {
                                    int encodeResult = _vpxEncoder.Encode(q, convertedFrame.Length, 1, ref encodedBuffer);

                                    if (encodeResult != 0)
                                    {
                                        logger.LogWarning("VPX encode of video sample failed.");
                                    }
                                }
                            }

                            stampedTestPattern.Dispose();
                            stampedTestPattern = null;

                            OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, VP8_TIMESTAMP_SPACING, encodedBuffer);

                            encodedBuffer = null;
                        }
                        else
                        {
                            _sendTestPatternTimer?.Dispose();
                            _sendTestPatternTimer = null;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendTestPattern. " + excp);
            }
        }

        private static byte[] BitmapToRGB24(Bitmap bitmap)
        {
            try
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var length = bitmapData.Stride * bitmapData.Height;

                byte[] bytes = new byte[length];

                // Copy bitmap to byte[]
                Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
                bitmap.UnlockBits(bitmapData);

                return bytes;
            }
            catch (Exception)
            {
                return new byte[] { };
            }
        }

        private static void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        {
            int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

            Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = StringAlignment.Center;

                using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
                {
                    using (var gPath = new GraphicsPath())
                    {
                        float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                        if (locationText != null)
                        {
                            gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                        }

                        gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                        g.FillPath(Brushes.White, gPath);
                        g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                    }
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                logger.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    logger.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    logger.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
