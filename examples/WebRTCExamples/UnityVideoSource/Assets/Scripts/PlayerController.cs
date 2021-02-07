using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Net;
using System.Threading;

public class PlayerController : MonoBehaviour
{
    private const int FRAMES_PER_SECOND = 30; // TODO: Measure instead of hard coding.

    public float speed = 0f;
    public TextMeshProUGUI countText;
    public GameObject winTextObject;

    private Rigidbody rb;
    private int count;
    private float movementX;
    private float movementY;

#pragma warning disable 0649
    [SerializeField] private Camera cam;
#pragma warning restore 0649

    private RenderTexture _mainCamDupRenderTexture;
    private Texture2D _mainCamTexture2D;
    private WebRTCPeer _webRtcPeer;

    void Awake()
    {
        SIPSorcery.LogFactory.Set(new UnityLoggerFactory());
        _webRtcPeer = new WebRTCPeer();
    }

    // Start is called before the first frame update
    async void Start()
    {
        rb = GetComponent<Rigidbody>();
        count = 0;

        SetCountText();
        winTextObject.SetActive(false);

        var texture = cam.targetTexture;
        _mainCamDupRenderTexture = texture;
        _mainCamTexture2D = new Texture2D(texture.width, texture.height);

        await _webRtcPeer.Start();
    }

    void OnMove(InputValue movementValue)
    {
        Vector2 movementVector = movementValue.Get<Vector2>();

        movementX = movementVector.x;
        movementY = movementVector.y;
    }

    void SetCountText()
    {
        countText.text = "Count: " + count.ToString();

        if (count >= 10)
        {
            winTextObject.SetActive(true);
        }
    }

    void FixedUpdate()
    {
        Vector3 movement = new Vector3(movementX, 0.0f, movementY);
        rb.AddForce(movement * speed);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("PickUp"))
        {
            other.gameObject.SetActive(false);
            count++;

            SetCountText();
        }
    }

    private void OnRenderObject()
    {
        RenderTexture.active = _mainCamDupRenderTexture;
        _mainCamTexture2D.ReadPixels(new Rect(0, 0, _mainCamTexture2D.width, _mainCamTexture2D.height), 0, 0);
        _mainCamTexture2D.Apply();
        RenderTexture.active = null;

        // This call to get the raw pixels seems to be the biggest performance hit. On my Win10 i7 machine
        // frame rate reduces from approx. 200 fps to around 20fps with this call.
        var arr = _mainCamTexture2D.GetRawTextureData();
        byte[] flipped = new byte[arr.Length];

        int width = _mainCamTexture2D.width;
        int height = _mainCamTexture2D.height;
        int pixelSize = 4;
        int stride = width * pixelSize;
        for (int row = height - 1; row >= 0; row--)
        {
            Buffer.BlockCopy(arr, row * stride, flipped, (height - row - 1) * stride, stride);
        }

        _webRtcPeer.VideoEncoderEndPoint.ExternalVideoSourceRawSample(FRAMES_PER_SECOND,
            _mainCamTexture2D.width,
            _mainCamTexture2D.height,
            flipped,
            VideoPixelFormatsEnum.Bgra);
    }

    private void OnApplicationQuit()
    {
        _webRtcPeer.Close("application exit");
    }
}

public class WebRTCPeer
{
    private const string REST_SIGNALING_SERVER = "https://sipsorcery.cloud/api/webrtcsignal";
    private const string REST_SIGNALING_MY_ID = "uni";
    private const string REST_SIGNALING_THEIR_ID = "bro";

    private static Microsoft.Extensions.Logging.ILogger logger;

    private WebRTCRestSignalingPeer _webrtcRestSignaling;
    public SIPSorceryMedia.Encoders.VideoEncoderEndPoint VideoEncoderEndPoint { get; }
    private CancellationTokenSource _cts;

    public WebRTCPeer()
    {
        logger = SIPSorcery.LogFactory.CreateLogger("webrtc");

        VideoEncoderEndPoint = new SIPSorceryMedia.Encoders.VideoEncoderEndPoint();
        _cts = new CancellationTokenSource();

        _webrtcRestSignaling = new WebRTCRestSignalingPeer(
            REST_SIGNALING_SERVER,
            REST_SIGNALING_MY_ID,
            REST_SIGNALING_THEIR_ID,
            this.CreatePeerConnection);
    }

    public Task Start()
    {
        return _webrtcRestSignaling.Start(_cts);
        //return Task.CompletedTask;
    }

    public void Close(string reason)
    {
        _cts.Cancel();
        _webrtcRestSignaling?.RTCPeerConnection?.Close(reason);
    }

    private Task<SIPSorcery.Net.RTCPeerConnection> CreatePeerConnection()
    {
        var pc = new SIPSorcery.Net.RTCPeerConnection(null);

        // Set up sources and hook up send events to peer connection.
        //AudioExtrasSource audioSrc = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.None });
        //audioSrc.OnAudioSourceEncodedSample += pc.SendAudio;
        //var testPatternSource = new VideoTestPatternSource();
        //testPatternSource.SetMaxFrameRate(true);
        //testPatternSource.OnVideoSourceRawSample += VideoEncoderEndPoint.ExternalVideoSourceRawSample;
        VideoEncoderEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;

        // Add tracks.
        //var audioTrack = new SIPSorcery.Net.MediaStreamTrack(audioSrc.GetAudioSourceFormats(), SIPSorcery.Net.MediaStreamStatusEnum.SendOnly);
        //pc.addTrack(audioTrack);
        var videoTrack = new SIPSorcery.Net.MediaStreamTrack(VideoEncoderEndPoint.GetVideoSourceFormats(), SIPSorcery.Net.MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);

        // Handlers to set the codecs to use on the sources once the SDP negotiation is complete.
        pc.OnVideoFormatsNegotiated += (sdpFormat) => VideoEncoderEndPoint.SetVideoSourceFormat(sdpFormat.First());
        //pc.OnAudioFormatsNegotiated += (sdpFormat) => audioSrc.SetAudioSourceFormat(sdpFormat.First());

        pc.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
        pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
        pc.onconnectionstatechange += (state) =>
        {
            logger.LogDebug($"Peer connection connected changed to {state}.");
            if (state == SIPSorcery.Net.RTCPeerConnectionState.connected)
            {
                //await audioSrc.StartAudio();
                //await testPatternSource.StartVideo();
            }
            else if (state == SIPSorcery.Net.RTCPeerConnectionState.closed || state == SIPSorcery.Net.RTCPeerConnectionState.failed)
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

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        Debug.Log("[" + eventId + "] " + formatter(state, exception));
        System.Diagnostics.Debug.WriteLine("[" + eventId + "] " + formatter(state, exception));
    }
}

