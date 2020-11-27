using System.IO;
using System.Net;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class MusicMediaSession : VoIPMediaSession
    {
        private readonly MediaStreamTrack _mediaStream;

        public MusicMediaSession(AudioFormat sourceAudioFormat, IPEndPoint destination) : base(new MediaEndPoints { AudioSource = new EmptyAudioSource() })
        {
            SetDestination(SDPMediaTypesEnum.audio, destination, null);
            AudioExtrasSource.SetAudioSourceFormat(sourceAudioFormat);
            AudioExtrasSource.SetSource(AudioSourcesEnum.Music);
            _mediaStream = new MediaStreamTrack(SDPMediaTypesEnum.audio, true, AudioLocalTrack.Capabilities);
        }

        public async Task SendAudioFile(string fileToPlay)
        {
            await AudioExtrasSource.SendAudioFromStream(
                        new FileStream(fileToPlay, FileMode.Open, FileAccess.Read, FileShare.Read), AudioSamplingRatesEnum.Rate8KHz);

        }
        public override MediaStreamTrack AudioRemoteTrack
        {
            get
            {
                return _mediaStream;
            }
        }
    }
}
