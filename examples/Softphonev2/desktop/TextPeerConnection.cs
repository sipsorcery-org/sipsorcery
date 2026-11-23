using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Net;

namespace SIPSorcery.Softphone;

/// <summary>
/// The SIP leg's peer connection on a WebRTC call, with real-time text on a data channel
/// (RFC 8865): a T.140 channel agreed in the SDP (RFC 8864) rather than opened in band, on the
/// m=application line every WebRTC data channel uses:
///
///   m=application 9 UDP/DTLS/SCTP webrtc-datachannel
///   a=dcmap:1 label="t140";subprotocol="t140"
///
/// RTCPeerConnection writes the m=application line whenever it has a data channel (or the offer
/// has one), and keeps a dcmap it's given as an unrecognised attribute, but doesn't write one of
/// its own: CreateOffer and CreateAnswer here add it. The channel is a negotiated one (no DCEP
/// open) on the stream the dcmap gives; its messages are T.140 text, as UTF-8 strings: letters,
/// backspaces and line separators, as they're typed. No redundancy: the channel is reliable.
/// </summary>
public sealed class TextPeerConnection : RTCPeerConnection
{
    public const string Subprotocol = "t140";
    /// <summary>The stream our offers use. Any will do: both ends open it from the SDP.</summary>
    public const ushort OfferStreamId = 1;

    /// <summary>The T.140 channel, once one's set up: opened from the SDP, or by the far end in band.</summary>
    public RTCDataChannel? T140 { get; private set; }
    /// <summary>The T.140 channel is open: raised once, on an SCTP thread.</summary>
    public event Action? TextOpened;
    /// <summary>Text from them, as it arrives.</summary>
    public event Action<string>? TextReceived;

    public TextPeerConnection()
    {
        // A far end that opens its T.140 channel in band (DCEP) rather than in the SDP.
        ondatachannel += dc =>
        {
            if (T140 == null && string.Equals(dc.protocol, Subprotocol, StringComparison.OrdinalIgnoreCase)) Use(dc);
        };
    }

    /// <summary>
    /// Sets up the T.140 channel on <paramref name="streamId"/>, before the offer or answer that
    /// carries its dcmap. It opens once the SCTP association is up.
    /// </summary>
    public void AddT140(ushort streamId)
    {
        if (T140 != null) return;
        var task = createDataChannel(Subprotocol, new RTCDataChannelInit { negotiated = true, id = streamId, protocol = Subprotocol });
        // Before the call's media is up createDataChannel adds a pending channel and returns at once.
        Use(task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult());
    }

    private void Use(RTCDataChannel dc)
    {
        T140 = dc;
        dc.onmessage += (_, _, data) => TextReceived?.Invoke(Encoding.UTF8.GetString(data));
        if (dc.IsOpened) TextOpened?.Invoke();
        else dc.onopen += () => TextOpened?.Invoke();
    }

    public bool TextOpen => T140?.IsOpened == true;

    public void SendText(string text)
    {
        if (TextOpen) T140!.send(text);
    }

    public override SDP CreateOffer(IPAddress connectionAddress) => WithDcmap(base.CreateOffer(connectionAddress));
    public override SDP CreateAnswer(IPAddress connectionAddress) => WithDcmap(base.CreateAnswer(connectionAddress));

    private SDP WithDcmap(SDP sdp)
    {
        if (sdp == null || T140?.id is not ushort id) return sdp!;
        var app = sdp.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.application && m.Port != 0);
        if (app == null) return sdp;
        app.ExtraMediaAttributes.RemoveAll(a => a.StartsWith("a=dcmap:") || a.StartsWith("a=dcsa:"));
        app.ExtraMediaAttributes.Add($"a=dcmap:{id} label=\"{Subprotocol}\";subprotocol=\"{Subprotocol}\"");
        return sdp;
    }

    private static readonly Regex Dcmap = new(@"^a=dcmap:(\d+)\s+(.*)$", RegexOptions.IgnoreCase);
    private static readonly Regex T140Subprotocol = new(@"(^|;)\s*subprotocol=""t140""", RegexOptions.IgnoreCase);

    /// <summary>The stream of the T.140 channel in an SDP's active data channel line, or null if it has none.</summary>
    public static ushort? FindT140(SDP? sdp)
    {
        var app = sdp?.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.application && m.Port != 0);
        if (app == null) return null;
        foreach (var a in app.ExtraMediaAttributes)
        {
            var m = Dcmap.Match(a.Trim());
            if (m.Success && T140Subprotocol.IsMatch(m.Groups[2].Value) && ushort.TryParse(m.Groups[1].Value, out var id) && id < 65535) return id;
        }
        return null;
    }

    public static ushort? FindT140(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp)) return null;
        try { return FindT140(SDP.ParseSDPDescription(sdp)); }
        catch { return null; }
    }
}
