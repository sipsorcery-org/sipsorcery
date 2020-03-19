//-----------------------------------------------------------------------------
// Filename: RtpAudioSession.cs
//
// Description: A lightweight audio only RTP session suitable for simple
// scenarios.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Media
{
    public class RtpAudioSession : RTPSession, IMediaSession
    {
        private const int DTMF_EVENT_DURATION = 1200;        // Default duration for a DTMF event.
        private const int DTMF_EVENT_PAYLOAD_ID = 101;

        public event Action<byte[], uint, uint, int> OnVideoSampleReady;
        public event Action<Complex[]> OnAudioScopeSampleReady;
        public event Action<Complex[]> OnHoldAudioScopeSampleReady;

        public RtpAudioSession(AddressFamily addressFamily) :
            base(addressFamily, false, false, false)
        {  }

        public void Close(string reason)
        {
            base.CloseSession(reason);
        }

        public Task SendDtmf(byte key, CancellationToken ct)
        {
            var dtmfEvent = new RTPEvent(key, false, RTPEvent.DEFAULT_VOLUME, DTMF_EVENT_DURATION, DTMF_EVENT_PAYLOAD_ID);
            return SendDtmfEvent(dtmfEvent, ct);
        }

        public void SendMedia(SDPMediaTypesEnum mediaType, uint samplePeriod, byte[] sample)
        {
            if(mediaType == SDPMediaTypesEnum.audio)
            {
                int payloadID = Convert.ToInt32(localDescription.sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).First().MediaFormats.First().FormatID);
                base.SendAudioFrame(samplePeriod, payloadID, sample);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public Task Start()
        {
            return Task.CompletedTask;
        }
    }
}
