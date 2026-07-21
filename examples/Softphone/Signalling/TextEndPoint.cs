using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SipLib.RealTimeText;
using SipLib.Rtp;
using SipLib.Sdp;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.SoftPhone.Signalling
{
    /// <summary>Implementation of a SIP endpoint for processing realtime text</summary>
    /// <remarks>
    /// TODO: support for RFC 9071<br/>
    /// NOTE: this endpoint is restricted to send and receive using the same text format only!
    /// </remarks>
    public class TextEndPoint : ITextEndPoint
    {
        private static ILogger _logger = SIPSorcery.LogFactory.CreateLogger<TextEndPoint>();
        private bool _sinkDisabled;
        private bool _sourceDisabled;
        private bool _sinkStarted;
        private bool _sourceStarted;
        private bool _sinkPaused;
        private bool _sourcePaused;
        private RttSender _rttSender = null;
        private RttReceiver _rttReceiver = null;
        private RttParameters _rttSinkParameters = null;
        private RttParameters _rttSourceParameters = null;
        private MediaFormatManager<TextFormat> _textFormatManager;

        /// <summary>Used to send text data</summary>
        public event EncodedTextSampleDelegate OnTextSourceEncodedSample;

        /// <summary>Incoming text</summary>
        public event EventHandler<TextEventArgs> OnTextReceived;

        public bool CanSendText => !_sourceDisabled && !_sourcePaused && _sourceStarted;

        /// <inheritdoc cref="TextEndPoint"/>
        /// <param name="disableSource">Disables text source: no text will be send from this endpoint</param>
        /// <param name="disableSink">Disables text sink: no text will be processed by this endpoint</param>
        public TextEndPoint(bool disableSource = false, bool disableSink = false)
        {
            _logger = SIPSorcery.LogFactory.CreateLogger<TextEndPoint>();
            _sourceDisabled = disableSource;
            _sinkDisabled = disableSink;
            _textFormatManager = new MediaFormatManager<TextFormat>(GetWellKnownTextFormats().ToList());

            SetTextSourceFormat(_textFormatManager.GetSourceFormats().First());
            SetTextSinkFormat(_textFormatManager.GetSourceFormats().First());
        }

        private IEnumerable<TextFormat> GetWellKnownTextFormats()
        {
            // default t140 format, using the example id 98 from RFC 4103
            yield return new TextFormat(TextCodecsEnum.T140, 98);

            // default red format, using the example id 100 from RFC 4103 and referring to
            // the content format 98 for the primary and two redundant text blocks
            yield return new TextFormat(TextCodecsEnum.RED, 100, parameters: "98/98/98");
        }

        /// <summary>Processes incoming data if sink is not disabled</summary>
        public void GotTextRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, int marker, byte[] payload)
        {
            if (_sinkDisabled || !_sinkStarted || _sinkPaused)
            {
                _logger.LogTrace("Incoming text purged, as sink is not running");
                return;
            }

            _logger.LogTrace($"Incoming text from '{remoteEndPoint.Address}:{remoteEndPoint.Port}': {payload.Length} bytes");
            RtpPacket rtpPacket = new(payload);
            rtpPacket.SSRC = ssrc;
            rtpPacket.Marker = marker > 0;
            rtpPacket.PayloadType = payloadID;
            rtpPacket.Timestamp = timestamp;

            try
            {
                rtpPacket.SequenceNumber = (ushort)seqnum;
            }
            catch (Exception)
            {
                rtpPacket.SequenceNumber = 0;
            }

            _rttReceiver.ProcessRtpPacket(rtpPacket);
        }

        /// <summary>Sends text if not disabled</summary>
        /// <exception cref="InvalidOperationException">If disabled</exception>
        public void SendText(string text)
        {
            if (_sourcePaused)
            {
                return;
            }

            DoIfRunning(() => _rttSender.SendMessage(text));
        }

        /// <summary>Enables receiving text with specified format</summary>
        public void SetTextSinkFormat(TextFormat textFormat)
        {
            _textFormatManager.SetSelectedFormat(textFormat);
            _rttSinkParameters = GetRttParams(textFormat);

            if (_rttReceiver != null)
            {
                _rttReceiver.RttCharactersReceived -= _rttReceiver_RttCharactersReceived;
            }

            _rttReceiver = new RttReceiver(_rttSinkParameters);
            _rttReceiver.RttCharactersReceived += _rttReceiver_RttCharactersReceived;

            _logger.LogInformation($"Set text sink format: {textFormat.FormatName}/{textFormat.Codec}");
        }

        /// <summary>Enables sending text with specified format</summary>
        public void SetTextSourceFormat(TextFormat textFormat)
        {
            _textFormatManager.SetSelectedFormat(textFormat);
            _rttSourceParameters = GetRttParams(textFormat);

            RttRtpSendDelegate virtualRttSender = new((RtpPacket data) => SendData(data.PacketBytes));

            _rttSender?.Stop();
            _rttSender = new RttSender(_rttSourceParameters, virtualRttSender);
            _rttSender.Start();

            _logger.LogInformation($"Set text source format: {textFormat.FormatName}/{textFormat.Codec}");
        }

        public TextFormat GetTextSinkFormat() => _textFormatManager.GetSourceFormats().FirstOrDefault();

        public TextFormat GetTextSourceFormat() => _textFormatManager.GetSourceFormats().FirstOrDefault();

        public Task Start()
        {
            var sourceTask = StartText();
            var sinkTask = StartTextSink();
            return Task.WhenAll(sourceTask, sinkTask);
        }

        public Task StartText()
        {
            if (_rttSender == null)
            {
                return Task.FromException(new InvalidOperationException("No source format specified"));
            }

            return DoIfRunning(() =>
            {
                _sourceStarted = true;
                _logger.LogInformation("Sending text started");
            }, throwIfNotRunning: true, ignoreStopped: true);
        }

        public Task StartTextSink()
        {
            if (_rttReceiver == null)
            {
                return Task.FromException(new InvalidOperationException("No sink format specified"));
            }

            return DoIfRunning(() =>
            {
                _sinkStarted = true;
                _logger.LogInformation("Receiving text started");
            }, isSource: false, throwIfNotRunning: true, ignoreStopped: true);
        }

        public Task Pause()
        {
            var sourceTask = PauseText();
            var sinkTask = PauseTextSink();
            return Task.WhenAll(sourceTask, sinkTask);
        }

        public Task PauseText()
        {
            return DoIfRunning(() =>
            {
                _sourcePaused = true;
                _rttSender.Stop();
            });
        }

        public Task PauseTextSink() => DoIfRunning(() => _sinkPaused = true, isSource: false);

        public Task Resume()
        {
            var sourceTask = ResumeText();
            var sinkTask = ResumeTextSink();
            return Task.WhenAll(sourceTask, sinkTask);
        }

        public Task ResumeText()
        {
            return DoIfRunning(() =>
            {
                _sourcePaused = false;
                _rttSender.Start();
            });
        }

        public Task ResumeTextSink() => DoIfRunning(() => _sinkPaused = false, isSource: false);

        public Task Close()
        {
            CloseText();
            CloseTextSink();

            // ignore errors at closing
            return Task.CompletedTask;
        }

        public Task CloseText()
        {
            _rttSender?.Stop();
            _sourceStarted = false;
            return Task.CompletedTask;
        }

        public Task CloseTextSink()
        {
            _sinkStarted = false;
            return Task.CompletedTask;
        }

        private void _rttReceiver_RttCharactersReceived(string RxChars, string Source)
            => OnTextReceived?.Invoke(this, new TextEventArgs(Source, RxChars));

        private void SendData(byte[] packetBytes) => OnTextSourceEncodedSample?.Invoke(packetBytes);

        private RttParameters GetRttParams(TextFormat textFormat)
        {
            RttParameters rttParams = new();

            switch (textFormat.Codec)
            {
                case TextCodecsEnum.Unknown:
                    break;
                case TextCodecsEnum.T140:
                    rttParams.T140PayloadType = textFormat.FormatID;
                    break;
                case TextCodecsEnum.RED:
                    rttParams.RedundancyPayloadType = textFormat.FormatID;
                    break;
                default:
                    break;
            }

            rttParams.Cps = 0;
            if (textFormat.Parameters != null)
            {
                SdpAttribute sdpAttributeFmtp = SdpAttribute.ParseSdpAttribute(textFormat.Parameters);
                string cps = null;
                if (sdpAttributeFmtp.GetAttributeParameter("cps", ref cps) && cps != null)
                {
                    _ = int.TryParse(cps, out rttParams.Cps);
                }
            }

            return rttParams;
        }

        private Task DoIfRunning(Action action, bool isSource = true, bool throwIfNotRunning = false, bool ignoreStopped = false)
        {
            bool isDisabled = isSource ? _sourceDisabled : _sinkDisabled;
            bool isStarted = ignoreStopped || (isSource ? _sourceStarted : _sinkStarted);
            string direction = isSource ? "source" : "sink";

            if (isDisabled || !isStarted)
            {
                string status = isDisabled ? "disabled" : "stopped";
                Exception error = new InvalidOperationException($"Text {direction} is {status}");
                if (throwIfNotRunning)
                {
                    throw error;
                }
                else
                {
                    return Task.FromException(error);
                }
            }
            else
            {
                action();
                return Task.CompletedTask;
            }
        }
    }
}
