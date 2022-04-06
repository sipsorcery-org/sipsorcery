using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media
{
    /// <summary>
    /// A media session that will send a generated audio signal or silence to the remote party
    /// and ignore any RTP it receives. This class is intended for testing scenarios.
    /// </summary>
    public class AudioSendOnlyMediaSession : RTPSession, IMediaSession
    {
        private static ILogger logger = SIPSorcery.Sys.Log.Logger;

        public AudioExtrasSource AudioExtrasSource { get; private set; }

        public AudioSendOnlyMediaSession(
            IPAddress bindAddress = null,
            int bindPort = 0)
            : base(false, false, false, bindAddress, bindPort)
        {
            // The audio extras source is used for on-hold music.
            AudioExtrasSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
            AudioExtrasSource.OnAudioSourceEncodedSample += SendAudio;

            base.OnAudioFormatsNegotiated += AudioFormatsNegotiated;

            var audioTrack = new MediaStreamTrack(AudioExtrasSource.GetAudioSourceFormats());
            base.addTrack(audioTrack);
        }

        private void AudioFormatsNegotiated(List<AudioFormat> audoFormats)
        {
            var audioFormat = audoFormats.First();
            logger.LogDebug($"Setting audio source format to {audioFormat.FormatID}:{audioFormat.Codec}.");
            AudioExtrasSource.SetAudioSourceFormat(audioFormat);
        }

        public async override Task Start()
        {
            if (!base.IsStarted)
            {
                await base.Start().ConfigureAwait(false);
                await AudioExtrasSource.StartAudio().ConfigureAwait(false);
            }
        }

        public async override void Close(string reason)
        {
            if (!base.IsClosed)
            {
                base.Close(reason);

                AudioExtrasSource.OnAudioSourceEncodedSample -= SendAudio;
                await AudioExtrasSource.CloseAudio().ConfigureAwait(false);
            }
        }
    }
}
