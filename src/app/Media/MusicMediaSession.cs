using System.IO;
using System.Net;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media
{
    /// <summary>
    /// A unidirectional RTPSession for sending music
    /// no mic or speaker support
    /// </summary>
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

        public Task SendAudioFile(string fileToPlay)
        {
            return AudioExtrasSource.SendAudioFromStream(
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
