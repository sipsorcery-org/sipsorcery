//-----------------------------------------------------------------------------
// Filename: WebRTCStream.cs
//
// Description: An example script to receive a WebRTC video stream onto a 
// Unity raw image. This sample uses an alpha version of VP8.Net (C# port
// of the VP8 codec in libvpx). The decoder still needs some more work but
// it is able to decode the key frames and prove the concept that a WebRTC
// video stream can be supplied to Unity with NO native libraries required.
//
// The server used in testing this example was the examples/WebRTCTestPatternServer
// dotnet run -- --rest==https://sipsorcery.cloud/api/webrtcsignal;svr;unity
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 22 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Net;
using Vpx.Net;

public class WebRTCStream : MonoBehaviour
{
    public const int VIDEO_FRAME_WIDTH = 640;
    public const int VIDEO_FRAME_HEIGHT = 480;

    private WebRTCPeer _webRtcPeer;
    private RawImage _webRTCStreamImage;
    private Texture2D _webRTCStreamTexture;
    private byte[] _textureBytes;
    private bool _newFrameReady;

    void Awake()
    {
        SIPSorcery.LogFactory.Set(new UnityLoggerFactory());
        _webRtcPeer = new WebRTCPeer();
    }

    async void Start()
    {
        _webRTCStreamImage = GetComponent<RawImage>();
        _webRTCStreamTexture = new Texture2D(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT);
        var rawBytes = _webRTCStreamTexture.GetRawTextureData();
        int rawBytesLength = rawBytes.Length;
        _textureBytes = new byte[rawBytesLength];
        _webRTCStreamImage.texture = _webRTCStreamTexture;

        _webRtcPeer.OnVideoFrame += UpdateTexture;
        await _webRtcPeer.Start();
    }

    // Start is called before the first frame update
    void Update()
    {
        //byte[] data = _textureBytes;
        //int pixel = 0;
        //int second = DateTime.Now.Second;
        //for (int y = 0; y < VIDEO_FRAME_HEIGHT; y++)
        //{
        //    for (int x = 0; x < VIDEO_FRAME_WIDTH; x++)
        //    {
        //        data[pixel] = (byte)((second % 2 == 0) ? 255 : 0);
        //        data[pixel + 1] = (byte)((second % 3 == 0) ? 255 : 0);
        //        data[pixel + 2] = (byte)((second % 4 == 0) ? 255 : 0);
        //        data[pixel + 3] = 255;

        //        pixel += 4;
        //    }
        //}
        if (_newFrameReady)
        {
            _webRTCStreamTexture.LoadRawTextureData(_textureBytes);
            _webRTCStreamTexture.Apply();
            _newFrameReady = false;
        }
    }

    void OnApplicationQuit()
    {
        _webRtcPeer.Close("application exit");
    }

    private void UpdateTexture(byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat)
    {
        //Buffer.BlockCopy(bmp, 0, _textureBytes, 0, bmp.Length);

        int posn = 0;
        for (int i = bmp.Length - 1; i > 0; i -= 3)
        {
            _textureBytes[posn++] = bmp[i];
            _textureBytes[posn++] = bmp[i - 1];
            _textureBytes[posn++] = bmp[i - 2];
            _textureBytes[posn++] = 255;
        }

        //SetDummyTexture(width, height);

        _newFrameReady = true;
    }

    private void SetDummyTexture(uint width, uint height)
    {
        int pixel = 0;
        int second = DateTime.Now.Second;
        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < height; x++)
            {
                _textureBytes[pixel] = (byte)((second % 2 == 0) ? 255 : 0);
                _textureBytes[pixel + 1] = (byte)((second % 3 == 0) ? 255 : 0);
                _textureBytes[pixel + 2] = (byte)((second % 4 == 0) ? 255 : 0);
                _textureBytes[pixel + 3] = 255;

                pixel += 4;
            }
        }
    }
}

public class WebRTCPeer
{
    private const string REST_SIGNALING_SERVER = "https://sipsorcery.cloud/api/webrtcsignal";
    private const string REST_SIGNALING_MY_ID = "unity";
    private const string REST_SIGNALING_THEIR_ID = "svr";

    private static Microsoft.Extensions.Logging.ILogger logger;

    private WebRTCRestSignalingPeer _webrtcRestSignaling;
    public Vp8NetVideoEncoderEndPoint VideoEncoderEndPoint { get; }
    private CancellationTokenSource _cts;

    public event VideoSinkSampleDecodedDelegate OnVideoFrame;

    public WebRTCPeer()
    {
        logger = SIPSorcery.LogFactory.CreateLogger("webrtc");

        VideoEncoderEndPoint = new Vp8NetVideoEncoderEndPoint();

        _cts = new CancellationTokenSource();

        _webrtcRestSignaling = new WebRTCRestSignalingPeer(
            REST_SIGNALING_SERVER,
            REST_SIGNALING_MY_ID,
            REST_SIGNALING_THEIR_ID,
            this.CreatePeerConnection);
    }

    public Task Start()
    {
        VideoEncoderEndPoint.OnVideoSinkDecodedSample += OnVideoFrame;
        return _webrtcRestSignaling.Start(_cts);
    }

    public void Close(string reason)
    {
        _cts.Cancel();
        _webrtcRestSignaling?.RTCPeerConnection?.Close(reason);
    }

    private Task<RTCPeerConnection> CreatePeerConnection()
    {
        var pc = new RTCPeerConnection();

        // Set up sources and hook up send events to peer connection.
        //AudioExtrasSource audioSrc = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.None });
        //audioSrc.OnAudioSourceEncodedSample += pc.SendAudio;
        //var testPatternSource = new VideoTestPatternSource();
        //testPatternSource.SetMaxFrameRate(true);
        //testPatternSource.OnVideoSourceRawSample += VideoEncoderEndPoint.ExternalVideoSourceRawSample;
        //VideoEncoderEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;

        // Add tracks.
        //var audioTrack = new SIPSorcery.Net.MediaStreamTrack(audioSrc.GetAudioSourceFormats(), SIPSorcery.Net.MediaStreamStatusEnum.SendOnly);
        //pc.addTrack(audioTrack);
        var videoTrack = new MediaStreamTrack(VideoEncoderEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.RecvOnly);
        pc.addTrack(videoTrack);

        // Handlers to set the codecs to use on the sources once the SDP negotiation is complete.
        pc.OnVideoFormatsNegotiated += (sdpFormat) => VideoEncoderEndPoint.SetVideoSourceFormat(sdpFormat.First());
        //pc.OnAudioFormatsNegotiated += (sdpFormat) => audioSrc.SetAudioSourceFormat(sdpFormat.First());
        pc.OnVideoFrameReceived += VideoEncoderEndPoint.GotVideoFrame;

        pc.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
        pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
        pc.onconnectionstatechange += (state) =>
        {
            logger.LogDebug($"Peer connection connected changed to {state}.");
            if (state == RTCPeerConnectionState.connected)
            {
                //await audioSrc.StartAudio();
                //await testPatternSource.StartVideo();
            }
            else if (state == RTCPeerConnectionState.closed || state == SIPSorcery.Net.RTCPeerConnectionState.failed)
            {
                //await audioSrc.CloseAudio();
                //await testPatternSource.CloseVideo();
            }
        };

        //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
        //{
        //    bool hasUseCandidate = msg.Attributes.Any(x => x.AttributeType == SIPSorcery.Net.STUNAttributeTypesEnum.UseCandidate);
        //    logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}, use candidate {hasUseCandidate}.");
        //};

        return Task.FromResult(pc);
    }
}

public class UnityLoggerFactory : IDisposable, ILoggerFactory
{
    /// <summary>
    /// Creates a new <see cref="ILogger"/> instance.
    /// </summary>
    /// <param name="categoryName">The category name for messages produced by the logger.</param>
    /// <returns>The <see cref="ILogger"/>.</returns>
    public virtual Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new UnityLogger();
    }

    /// <summary>
    /// Adds an <see cref="ILoggerProvider"/> to the logging system.
    /// </summary>
    /// <param name="provider">The <see cref="ILoggerProvider"/>.</param>
    public virtual void AddProvider(ILoggerProvider provider)
    { }

    public void Dispose()
    { }
}

public class UnityLogger : IDisposable, Microsoft.Extensions.Logging.ILogger
{
    public IDisposable BeginScope<TState>(TState state)
    {
        return this;
    }

    public void Dispose()
    {
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        Debug.Log("[" + eventId + "] " + formatter(state, exception));
        System.Diagnostics.Debug.WriteLine("[" + eventId + "] " + formatter(state, exception));
    }
}
