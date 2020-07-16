//-----------------------------------------------------------------------------
// Filename: RTCDataChannel.cs
//
// Description: Contains an implementation for a WebRTC data channel.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Jul 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public class RTCDataChannel : IRTCDataChannel
    {
        public string label { get; set; }

        public bool ordered { get; set; }

        public ushort? maxPacketLifeTime { get; set; }

        public ushort? maxRetransmits { get; set; }

        public string protocol { get; set; }

        public bool negotiated { get; set; }

        public ushort? id { get; set; }

        public RTCDataChannelState readyState => RTCDataChannelState.connecting;

        public ulong bufferedAmount { get; set; }

        public ulong bufferedAmountLowThreshold { get; set; }
        public string binaryType { get; set; }

        public long MaxMessageSize { get; set; }

        public int MLineIndex { get; set; }

        public string MediaID { get; set; }

        public event Action onopen;
        public event Action onbufferedamountlow;
        public event Action onerror;
        public event Action onclosing;
        public event Action onclose;
        public event Action onmessage;

        public void close()
        {
            onclose?.Invoke();
        }

        public void send(string data)
        {

        }

        public void send(byte[] data)
        {

        }
    }
}
