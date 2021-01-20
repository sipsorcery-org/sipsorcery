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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.Sctp;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTCDataChannel : SCTPStreamListener, IRTCDataChannel
    {
        private static ILogger logger = Log.Logger;

        public string label { get; set; }

        public bool ordered { get; set; }

        public ushort? maxPacketLifeTime { get; set; }

        public ushort? maxRetransmits { get; set; }

        public string protocol { get; set; }

        public bool negotiated { get; set; }

        public ushort? id { get; set; }

        public RTCDataChannelState readyState { get; private set; } = RTCDataChannelState.connecting;

        public ulong bufferedAmount { get; set; }

        public ulong bufferedAmountLowThreshold { get; set; }
        public string binaryType { get; set; }

        //public long MaxMessageSize { get; set; }

        private SCTPStream _sctpStream;

        public string Error { get; private set; }

        public bool IsOpened { get; private set; } = false;

        public event Action onopen;
        //public event Action onbufferedamountlow;
        public event Action<string> onerror;
        //public event Action onclosing;
        public event Action onclose;
        public event Action<string> onmessage;
        public event Action<byte[]> onDatamessage;

        internal void SetStream(SCTPStream s)
        {
            _sctpStream = s;
            s.setSCTPStreamListener(this);
            s.OnOpen = OnStreamOpened;
        }

        internal void OnStreamOpened()
        {
            logger.LogDebug($"Data channel for label {label} now open.");
            IsOpened = true;
            id = (ushort)_sctpStream.getNum();
            readyState = RTCDataChannelState.open;
            onopen?.Invoke();
        }

        /// <summary>
        /// Sets the error message is there was a problem creating the data channel.
        /// </summary>
        internal void SetError(string error)
        {
            Error = error;
            onerror?.Invoke(error);
        }

        public void close()
        {
            IsOpened = false;
            readyState = RTCDataChannelState.closing;
            _sctpStream?.close();
            readyState = RTCDataChannelState.closed;
        }

        public void send(string data)
        {
            if (!IsOpened)
            {
                logger.LogWarning("An attempt was made to send on a closed data channel.");
            }
            else
            {
                _sctpStream.send(data);
            }
        }

        public void send(byte[] data)
        {
            if (!IsOpened)
            {
                logger.LogWarning("An attempt was made to send on a closed data channel.");
            }
            else
            {
                _sctpStream.send(data);
            }
        }

        public Task sendasync(string data)
        {
            if (!IsOpened)
            {
                logger.LogWarning("An attempt was made to send on a closed data channel.");
                return Task.CompletedTask;
            }
            else
            {
                _sctpStream.send(data);
                return Task.CompletedTask;
            }
        }

        public Task sendasync(byte[] data)
        {
            if (!IsOpened)
            {
                logger.LogWarning("An attempt was made to send on a closed data channel.");
                return Task.CompletedTask;
            }
            else
            {
                _sctpStream.send(data);
                return Task.CompletedTask;
            }
        }

        public void close(SCTPStream s)
        {
            IsOpened = false;
            logger.LogDebug($"Data channel stream closed id {s.getNum()}.");
            onclose?.Invoke();
        }

        public void onDataMessage(SCTPStream s, byte[] data)
        {
            //logger.LogDebug($"Data channel received message (label={s.getLabel()}, streamID={s.getNum()}): {message}.");
            onDatamessage?.Invoke(data);
        }

        public void onMessage(SCTPStream s, string message)
        {
            //logger.LogDebug($"Data channel received message (label={s.getLabel()}, streamID={s.getNum()}): {message}.");
            onmessage?.Invoke(message);
        }
    }
}
