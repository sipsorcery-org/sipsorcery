using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTPReorderBuffer
    {
        private readonly TimeSpan _maxDropOutTime;
        private readonly IDateTime _datetimeProvider;
        private readonly System.Collections.Generic.LinkedList<RTPPacket> _data = new System.Collections.Generic.LinkedList<RTPPacket>();
        private ushort? _currentSeqNumber;

        private static ILogger logger = Log.Logger;

        public RTPReorderBuffer(TimeSpan maxDropOutTime, IDateTime datetimeProvider = null)
        {
            _maxDropOutTime = maxDropOutTime;
            _datetimeProvider = datetimeProvider ?? new DefaultTimeProvider();
        }

        private RTPPacket First => _data.First?.Value;
        private RTPPacket Last => _data.Last?.Value;

        private bool IsBeforeWrapAround(RTPPacket packet) {
            return IsBeforeWrapAround(packet.Header.SequenceNumber);
        }
        private bool IsBeforeWrapAround(ushort seq)
        {
            return seq > ushort.MaxValue / 2 + ushort.MaxValue / 4;
        }
        private bool IsAfterWrapAround(RTPPacket packet)
        {
            return packet.Header.SequenceNumber < ushort.MaxValue / 4;
        }

        public bool Get(out RTPPacket packet)
        {
            packet = null;
            if (Last == null)
            {
                return false;
            }

            if (_currentSeqNumber.HasValue && _currentSeqNumber != Last.Header.SequenceNumber)
            {

                if (_datetimeProvider.Time - Last.Header.ReceivedTime < _maxDropOutTime)
                {
                    return false;
                }
            }
            packet = Last;
            _data.RemoveLast();
            _currentSeqNumber = (ushort)checked(packet.Header.SequenceNumber + 1);
            return true;
        }

        public void Add(RTPPacket current)
        {
            if (_data.Count == 0)
            {
                _data.AddFirst(current);
                return;
            }

            // if seq number is greater or equal than we are waiting for then append to last position
            if (_currentSeqNumber.HasValue && _currentSeqNumber >= current.Header.SequenceNumber) {
                if (Last.Header.SequenceNumber > _currentSeqNumber || IsAfterWrapAround(Last) && IsBeforeWrapAround(_currentSeqNumber.Value)) {
                    _data.AddLast(current);
                    return;
                }
            }
            
            if (IsBeforeWrapAround(Last) && !IsAfterWrapAround(First) && IsAfterWrapAround(current)) // first incoming packet after wraparound
            {
                _data.AddFirst(current);
                return;
            }

            var node = _data.First;
            do
            {
                // if it is packet before wrap around skip all packets after wrap around and then insert the packet
                if (IsBeforeWrapAround(current) && IsBeforeWrapAround(Last) && IsAfterWrapAround(node.Value))
                {
                    node = node.Next;
                    continue;
                }
                if (IsBeforeWrapAround(node.Value) && IsAfterWrapAround(current))
                {
                    _data.AddBefore(node, current);
                    break;
                }
                if (current.Header.SequenceNumber > node.Value.Header.SequenceNumber)
                {
                    _data.AddBefore(node, current);
                    break;
                }
                if (current.Header.SequenceNumber == node.Value.Header.SequenceNumber)
                {
                    logger.LogInformation("Duplicate seq number: {SequenceNumber}", current.Header.SequenceNumber);
                    break;
                }

                node = node.Next;
            }
            while (node != null);
        }
    }

    public interface IDateTime
    {
        DateTime Time { get; }
    }

    public class DefaultTimeProvider : IDateTime
    {
        public DateTime Time => DateTime.Now;
    }
}
