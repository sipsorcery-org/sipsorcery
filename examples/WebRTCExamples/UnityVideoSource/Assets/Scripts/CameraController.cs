using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CameraController : MonoBehaviour
{
    public GameObject player;
    private Vector3 offset;

    //private void Awake()
    //{
        //WebRTC.Initialize();
    //}

    // Start is called before the first frame update
    void Start()
    {
        offset = transform.position - player.transform.position;

        //var pc = new RTCPeerConnection();

        //var camera = GetComponent<Camera>();
        //var track = camera.CaptureStreamTrack(1280, 720, 250000);
        //pc.AddTrack(track);

        ////var audioStream = Audio.CaptureStream();
        ////pc.AddTrack(audioStream.GetTracks().First());

        //RTCOfferOptions rtcOfferOptions = default;// new RTCOfferOptions { offerToReceiveAudio = true };
        //var offerOp = pc.CreateOffer(ref rtcOfferOptions);
        //var offerSdp = offerOp.Desc;
        //pc.SetLocalDescription(ref offerSdp);

        //System.Diagnostics.Debug.WriteLine($"Our Offer:\n{offerSdp.sdp}");
    }

    void LateUpdate()
    {
        transform.position = player.transform.position + offset;
    }
}
