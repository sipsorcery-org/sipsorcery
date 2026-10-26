//-----------------------------------------------------------------------------
// Filename: RttEndPoint.cs
//
// Description: A real-time text (RTT) source and sink implementing RFC 4103
// T.140 text over RTP, with RED redundancy by default.
//
// RFC 4103 "RTP Payload for Text Conversation":
// https://www.rfc-editor.org/rfc/rfc4103
//
// Author(s):
// Claude Code
//
// History:
// 20 Oct 2026  Claude Code    Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media
{
    /// <summary>
    /// Sends and receives real-time text. Text passed to <see cref="SendText"/> is buffered and sent
    /// every buffer interval, and text received from the remote party is raised with
    /// <see cref="OnTextReceived"/>.
    /// </summary>
    /// <remarks>
    /// Typical use with a <see cref="VoIPMediaSession"/>:
    /// <code>
    /// var rtt = new RttEndPoint();
    /// var session = new VoIPMediaSession(new MediaEndPoints { TextSource = rtt, TextSink = rtt });
    /// rtt.OnTextReceived += text => Console.Write(text);
    /// rtt.SendText("hello");
    /// </code>
    /// T.140 editing characters, such as backspace (U+0008) and line separator (U+2028), are passed
    /// through unchanged in both directions; interpreting them is up to the application.
    /// </remarks>
    public class RttEndPoint : ITextEndPoint, IDisposable
    {
        public const int DEFAULT_T140_PAYLOAD_TYPE = 98;
        public const int DEFAULT_RED_PAYLOAD_TYPE = 100;

        /// <summary>
        /// The most characters held for text received before the sink is started.
        /// </summary>
        public const int MAX_TEXT_RECEIVED_BEFORE_START = 4096;

        private static readonly ILogger logger = LogFactory.CreateLogger<RttEndPoint>();

        private readonly int _bufferTimeMilliseconds;
        private readonly int _charactersPerSecond;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _timerSync = new object();
        private readonly object _sinkSync = new object();
        private readonly List<TextEventArgs> _textReceivedBeforeStart = new List<TextEventArgs>();

        private TextFormat _sourceFormat;
        private TextFormat _sinkFormat;
        private T140Packetiser _packetiser;
        private T140Depacketiser _depacketiser;
        private Timer _sendTimer;
        private bool _sourceStarted;
        private bool _sourcePaused;
        private bool _sinkStarted;
        private bool _sinkPaused;
        private bool _sinkClosed;

        /// <summary>
        /// Fires with the RTP payloads to send to the remote party.
        /// </summary>
        public event EncodedTextSampleDelegate OnTextSourceEncodedSample;

        /// <summary>
        /// Fires when new text is received from the remote party. Unrecoverable loss is shown
        /// with <see cref="T140Depacketiser.LOSS_MARKER"/>.
        /// </summary>
        public event Action<TextEventArgs> OnTextReceived;

        /// <summary>
        /// The number of received packets whose text was recovered from redundancy.
        /// </summary>
        public int RecoveredPacketCount => _depacketiser.RecoveredPacketCount;

        /// <summary>
        /// The number of received packets that were lost and could not be recovered.
        /// </summary>
        public int LostPacketCount => _depacketiser.LostPacketCount;

        /// <summary>
        /// True if text passed to <see cref="SendText"/> will be sent, i.e. the source has been
        /// started and is not paused, for example by the call being put on hold.
        /// </summary>
        public bool CanSendText => _sourceStarted && !_sourcePaused;

        /// <param name="useRedundancy">If true RED is offered in preference to plain t140.</param>
        /// <param name="t140PayloadType">The payload type to offer for t140.</param>
        /// <param name="redPayloadType">The payload type to offer for RED.</param>
        /// <param name="redundancy">The number of redundant generations, RFC 4103 recommends 2.</param>
        /// <param name="bufferTimeMilliseconds">How often buffered text is sent, RFC 4103 recommends 300ms.</param>
        /// <param name="charactersPerSecond">Limit on how fast text is sent, 0 for no limit.</param>
        public RttEndPoint(
            bool useRedundancy = true,
            int t140PayloadType = DEFAULT_T140_PAYLOAD_TYPE,
            int redPayloadType = DEFAULT_RED_PAYLOAD_TYPE,
            int redundancy = T140Packetiser.DEFAULT_REDUNDANCY,
            int bufferTimeMilliseconds = T140Packetiser.DEFAULT_BUFFER_TIME_MILLISECONDS,
            int charactersPerSecond = 0)
        {
            _bufferTimeMilliseconds = bufferTimeMilliseconds;
            _charactersPerSecond = charactersPerSecond;

            var defaultFormat = useRedundancy
                ? new TextFormat(TextCodecsEnum.RED, redPayloadType, parameters: T140RedPayload.BuildFmtp(t140PayloadType, redundancy))
                : new TextFormat(TextCodecsEnum.T140, t140PayloadType);

            SetTextSourceFormat(defaultFormat);
            SetTextSinkFormat(defaultFormat);
        }

        /// <summary>
        /// Queues text to send to the remote party.
        /// </summary>
        public void SendText(string text)
        {
            if (_sourcePaused)
            {
                logger.LogDebug("RTT source paused, text not sent.");
                return;
            }

            _packetiser.Enqueue(text);
        }

        public void GotTextRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, int marker, byte[] payload)
        {
            if (_sinkPaused || _sinkClosed)
            {
                return;
            }

            var text = _depacketiser.ProcessPayload(ssrc, (ushort)seqnum, payloadID, payload);

            if (text.Length == 0)
            {
                return;
            }

            var textData = new TextEventArgs(ssrc, text, new DateTime(timestamp));

            lock (_sinkSync)
            {
                if (!_sinkStarted)
                {
                    // The remote party can start sending as soon as it has answered, which can be before
                    // this side has started its media session, so early text is held until the sink starts.
                    if (_textReceivedBeforeStart.Select(e => e.Text.Length).Sum() + text.Length <= MAX_TEXT_RECEIVED_BEFORE_START)
                    {
                        _textReceivedBeforeStart.Add(textData);
                    }
                    else
                    {
                        logger.LogWarning("RTT text received before the sink was started exceeded {MaxChars} characters, text discarded.", MAX_TEXT_RECEIVED_BEFORE_START);
                    }

                    return;
                }
            }

            OnTextReceived?.Invoke(textData);
        }

        public TextFormat GetTextSourceFormat() => _sourceFormat;

        public TextFormat GetTextSinkFormat() => _sinkFormat;

        /// <summary>
        /// Sets the format used for sending. A RED format must have fmtp parameters listing the
        /// t140 payload type it carries, e.g. "98/98/98".
        /// </summary>
        public void SetTextSourceFormat(TextFormat textFormat)
        {
            GetPayloadTypes(textFormat, out int t140PayloadType, out int redPayloadType, out int redundancy);

            var previous = _packetiser;
            _packetiser = new T140Packetiser(t140PayloadType, redPayloadType, redundancy, _charactersPerSecond, _bufferTimeMilliseconds);
            _sourceFormat = textFormat;

            if (previous != null && previous.HasDataToSend)
            {
                logger.LogWarning("RTT source format changed with unsent text, the unsent text was discarded.");
            }

            logger.LogDebug("RTT source format set to {FormatName} t140 {T140PayloadType} red {RedPayloadType} redundancy {Redundancy}.",
                textFormat.FormatName, t140PayloadType, redPayloadType, redundancy);
        }

        /// <summary>
        /// Sets the format expected for receiving. With a RED format both RED and plain t140
        /// packets are accepted.
        /// </summary>
        public void SetTextSinkFormat(TextFormat textFormat)
        {
            GetPayloadTypes(textFormat, out int t140PayloadType, out int redPayloadType, out _);

            _depacketiser = new T140Depacketiser(t140PayloadType, redPayloadType);
            _sinkFormat = textFormat;
        }

        public Task Start()
        {
            StartTextSink();
            return StartText();
        }

        public Task StartText()
        {
            lock (_timerSync)
            {
                if (!_sourceStarted)
                {
                    _sourceStarted = true;
                    _sendTimer = new Timer(SendTimerTick, null, _bufferTimeMilliseconds, _bufferTimeMilliseconds);
                }
            }

            return Task.CompletedTask;
        }

        public Task StartTextSink()
        {
            List<TextEventArgs> earlyText;

            lock (_sinkSync)
            {
                _sinkStarted = true;
                _sinkClosed = false;
                earlyText = new List<TextEventArgs>(_textReceivedBeforeStart);
                _textReceivedBeforeStart.Clear();
            }

            foreach (TextEventArgs e in earlyText)
            {
                if (e.Text.Length > 0)
                {
                    OnTextReceived?.Invoke(e);
                }
            }

            return Task.CompletedTask;
        }

        public Task Pause()
        {
            PauseTextSink();
            return PauseText();
        }

        public Task PauseText()
        {
            _sourcePaused = true;
            return Task.CompletedTask;
        }

        public Task PauseTextSink()
        {
            _sinkPaused = true;
            return Task.CompletedTask;
        }

        public Task Resume()
        {
            ResumeTextSink();
            return ResumeText();
        }

        public Task ResumeText()
        {
            _sourcePaused = false;
            return Task.CompletedTask;
        }

        public Task ResumeTextSink()
        {
            _sinkPaused = false;
            return Task.CompletedTask;
        }

        public Task Close()
        {
            CloseTextSink();
            return CloseText();
        }

        public Task CloseText()
        {
            lock (_timerSync)
            {
                _sourceStarted = false;
                _sendTimer?.Dispose();
                _sendTimer = null;
            }

            return Task.CompletedTask;
        }

        public Task CloseTextSink()
        {
            lock (_sinkSync)
            {
                _sinkStarted = false;
                _sinkClosed = true;
                _textReceivedBeforeStart.Clear();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Close();
        }

        private void SendTimerTick(object state)
        {
            try
            {
                // Pausing stops new text being queued but anything already queued, and its
                // redundancy, is still flushed.
                var packet = _packetiser.GetNextPacket(_clock.ElapsedMilliseconds);

                if (packet.HasValue)
                {
                    OnTextSourceEncodedSample?.Invoke(packet.Value.Payload);
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception sending RTT. {ErrorMessage}", excp.Message);
            }
        }

        private static void GetPayloadTypes(TextFormat textFormat, out int t140PayloadType, out int redPayloadType, out int redundancy)
        {
            if (textFormat.Codec == TextCodecsEnum.RED)
            {
                if (!T140RedPayload.TryParseFmtp(textFormat.Parameters, out t140PayloadType, out redundancy))
                {
                    throw new ArgumentException($"The RED text format parameters \"{textFormat.Parameters}\" do not identify a t140 payload type.", nameof(textFormat));
                }

                redPayloadType = textFormat.FormatID;
            }
            else if (textFormat.Codec == TextCodecsEnum.T140)
            {
                t140PayloadType = textFormat.FormatID;
                redPayloadType = -1;
                redundancy = 0;
            }
            else
            {
                throw new ArgumentException($"Text format {textFormat.FormatName} is not supported for real-time text.", nameof(textFormat));
            }
        }
    }
}
