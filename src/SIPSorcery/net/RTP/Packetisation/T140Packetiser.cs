//-----------------------------------------------------------------------------
// Filename: T140Packetiser.cs
//
// Description: Sender and receiver logic for T.140 real-time text carried over
// RTP, with optional RED redundancy.
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
using System.Text;

namespace SIPSorcery.Net;

/// <summary>
/// An RTP payload produced by the <see cref="T140Packetiser"/>.
/// </summary>
public readonly struct T140Packet
{
    public byte[] Payload { get; }

    /// <summary>
    /// True for the first packet after an idle period, RFC 4103 section 3.
    /// </summary>
    public bool Marker { get; }

    public T140Packet(byte[] payload, bool marker)
    {
        Payload = payload;
        Marker = marker;
    }
}

/// <summary>
/// Buffers typed text and turns it into T.140 RTP payloads, RFC 4103 section 5.
/// The caller is responsible for calling <see cref="GetNextPacket"/> every buffer
/// interval (300ms is recommended) and sending whatever it returns.
/// </summary>
public class T140Packetiser
{
    public const int DEFAULT_BUFFER_TIME_MILLISECONDS = 300;
    public const int DEFAULT_REDUNDANCY = 2;

    private readonly object _sync = new object();
    private readonly StringBuilder _pending = new StringBuilder();
    private readonly List<(byte[] data, long timestamp)> _history = new List<(byte[], long)>();
    private int _flushRemaining;
    private bool _idle = true;

    public int T140PayloadType { get; }

    /// <summary>
    /// The RED payload type or -1 if redundancy is not being used.
    /// </summary>
    public int RedPayloadType { get; }

    /// <summary>
    /// The number of redundant generations sent in each RED packet.
    /// </summary>
    public int Redundancy { get; }

    /// <summary>
    /// The maximum characters per second the remote party accepts, the SDP "cps" parameter.
    /// 0 means no limit is applied.
    /// </summary>
    public int CharactersPerSecond { get; }

    public int BufferTimeMilliseconds { get; }

    public bool UsesRed => RedPayloadType >= 0;

    public T140Packetiser(int t140PayloadType, int redPayloadType = -1, int redundancy = DEFAULT_REDUNDANCY,
        int charactersPerSecond = 0, int bufferTimeMilliseconds = DEFAULT_BUFFER_TIME_MILLISECONDS)
    {
        T140PayloadType = t140PayloadType;
        RedPayloadType = redPayloadType;
        Redundancy = redPayloadType >= 0 ? Math.Max(0, redundancy) : 0;
        CharactersPerSecond = Math.Max(0, charactersPerSecond);
        BufferTimeMilliseconds = bufferTimeMilliseconds;
    }

    /// <summary>
    /// Queues text for sending. T.140 control characters, such as backspace (U+0008) and
    /// line separator (U+2028), are sent unchanged.
    /// </summary>
    public void Enqueue(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            lock (_sync)
            {
                _pending.Append(text);
            }
        }
    }

    /// <summary>
    /// True if there is queued text or redundant data that still needs sending.
    /// </summary>
    public bool HasDataToSend
    {
        get
        {
            lock (_sync)
            {
                return _pending.Length > 0 || _flushRemaining > 0;
            }
        }
    }

    /// <summary>
    /// Gets the payload for the next buffer interval.
    /// </summary>
    /// <param name="nowMilliseconds">A millisecond clock, used for the RED timestamp offsets.</param>
    /// <returns>The packet to send or null if there is nothing to send this interval.</returns>
    public T140Packet? GetNextPacket(long nowMilliseconds)
    {
        lock (_sync)
        {
            if (_pending.Length == 0 && _flushRemaining == 0)
            {
                // Transmission stops once all text has been sent with its full redundancy.
                _idle = true;
                _history.Clear();
                return null;
            }

            byte[] primary = TakePendingBytes();
            bool marker = _idle;
            _idle = false;

            if (!UsesRed)
            {
                return new T140Packet(primary, marker);
            }

            // Oldest generation first, padded with empty generations when the history is
            // shorter than the redundancy level, e.g. at the start of a transmission.
            var redundantBlocks = new List<T140RedBlock>(Redundancy);

            for (int i = 0; i < Redundancy - _history.Count; i++)
            {
                redundantBlocks.Add(new T140RedBlock(T140PayloadType, 0, Array.Empty<byte>()));
            }

            foreach (var (data, timestamp) in _history)
            {
                int offset = (int)Math.Min(Math.Max(0, nowMilliseconds - timestamp), T140RedPayload.MAX_TIMESTAMP_OFFSET);
                redundantBlocks.Add(new T140RedBlock(T140PayloadType, offset, data));
            }

            var payload = T140RedPayload.Encode(redundantBlocks, new T140RedBlock(T140PayloadType, 0, primary));

            _history.Add((primary, nowMilliseconds));
            if (_history.Count > Redundancy)
            {
                _history.RemoveAt(0);
            }

            _flushRemaining = primary.Length > 0 ? Redundancy : _flushRemaining - 1;

            return new T140Packet(payload, marker);
        }
    }

    /// <summary>
    /// Removes as much pending text as fits in one block, respecting the cps limit and the
    /// RED block length, without splitting a surrogate pair.
    /// </summary>
    private byte[] TakePendingBytes()
    {
        int maxChars = CharactersPerSecond > 0
            ? Math.Max(1, CharactersPerSecond * BufferTimeMilliseconds / 1000)
            : int.MaxValue;

        int chars = 0;
        int index = 0;
        int byteCount = 0;

        while (index < _pending.Length && chars < maxChars)
        {
            int width = char.IsHighSurrogate(_pending[index]) && index + 1 < _pending.Length ? 2 : 1;
            int codePointBytes = width == 2 ? 4 : _pending[index] < 0x80 ? 1 : _pending[index] < 0x800 ? 2 : 3;

            if (byteCount + codePointBytes > T140RedPayload.MAX_BLOCK_LENGTH)
            {
                break;
            }

            byteCount += codePointBytes;
            index += width;
            chars++;
        }

        string chunk = _pending.ToString(0, index);
        _pending.Remove(0, index);
        return Encoding.UTF8.GetBytes(chunk);
    }
}

/// <summary>
/// Turns received T.140 RTP payloads back into text, recovering lost packets from
/// RED redundancy where possible, RFC 4103 sections 4 and 5.
/// </summary>
public class T140Depacketiser
{
    /// <summary>
    /// Inserted where text was lost and could not be recovered, RFC 4103 section 5.4.
    /// </summary>
    public const char LOSS_MARKER = '\uFFFD';

    /// <summary>
    /// The zero width no-break space T.140 uses as a start of transmission and keep-alive
    /// marker. It carries no text so it is removed.
    /// </summary>
    public const char BYTE_ORDER_MARK = '\uFEFF';

    private readonly object _sync = new object();
    private bool _started;
    private uint _ssrc;
    private ushort _lastSeq;

    public int T140PayloadType { get; }

    /// <summary>
    /// The RED payload type or -1 if redundancy is not being used.
    /// </summary>
    public int RedPayloadType { get; }

    /// <summary>
    /// The number of packets that were lost and whose text was recovered from redundancy.
    /// </summary>
    public int RecoveredPacketCount { get; private set; }

    /// <summary>
    /// The number of packets that were lost and could not be recovered.
    /// </summary>
    public int LostPacketCount { get; private set; }

    public T140Depacketiser(int t140PayloadType, int redPayloadType = -1)
    {
        T140PayloadType = t140PayloadType;
        RedPayloadType = redPayloadType;
    }

    /// <summary>
    /// Processes a received RTP payload.
    /// </summary>
    /// <returns>The new text, which can be empty, for example for a keep-alive, duplicate or
    /// redundancy only packet.</returns>
    public string ProcessPayload(uint ssrc, ushort sequenceNumber, int payloadType, byte[] payload)
    {
        List<byte[]> generations;

        if (RedPayloadType >= 0 && payloadType == RedPayloadType)
        {
            if (!T140RedPayload.TryDecode(payload, out var redundantBlocks, out var primary))
            {
                return string.Empty;
            }

            generations = new List<byte[]>(redundantBlocks.Count + 1);
            foreach (var block in redundantBlocks)
            {
                generations.Add(block.Data);
            }
            generations.Add(primary.Data);
        }
        else
        {
            generations = new List<byte[]> { payload ?? Array.Empty<byte>() };
        }

        // The primary is the last generation, the one before it belongs to sequenceNumber - 1 and so on.
        int redundancy = generations.Count - 1;
        var text = new StringBuilder();

        lock (_sync)
        {
            if (!_started || ssrc != _ssrc)
            {
                // Nothing has been received from this source yet so every generation is new text.
                _started = true;
                _ssrc = ssrc;

                foreach (var generation in generations)
                {
                    text.Append(Encoding.UTF8.GetString(generation));
                }
            }
            else
            {
                ushort diff = (ushort)(sequenceNumber - _lastSeq);

                if (diff == 0 || diff >= 0x8000)
                {
                    // Duplicate or late packet, its text has already been delivered or recovered.
                    return string.Empty;
                }

                int missing = diff - 1;

                if (missing > redundancy)
                {
                    text.Append(LOSS_MARKER);
                    LostPacketCount += missing - redundancy;
                }

                int recoverable = Math.Min(missing, redundancy);
                RecoveredPacketCount += recoverable;

                for (int generation = redundancy - recoverable; generation <= redundancy; generation++)
                {
                    text.Append(Encoding.UTF8.GetString(generations[generation]));
                }
            }

            _lastSeq = sequenceNumber;
        }

        return text.Replace(BYTE_ORDER_MARK.ToString(), string.Empty).ToString();
    }
}
