// A stand-in for the network on leg B: a UDP relay between SIPSorcery's peer connection and page
// B's, which drops video RTP packets on their way to the page, as a lossy network would. They're
// dropped after SIPSorcery has sent them, so its resend buffer has them and page B's NACKs can be
// answered. STUN, DTLS, RTCP and audio always get through.
//
// Each side is told the relay is the other: the offer's candidates are replaced with the relay's
// browser-facing port and the answer's with its SIPSorcery-facing port, so ICE, DTLS and all the
// media go through it. Everything is on this machine, so the relay listens on the address the
// offer's own host candidate uses.

using System.Net;
using System.Net.Sockets;

namespace SIPSorcery.TranscodeLoop;

internal sealed class LossyLink : IDisposable
{
    private readonly Socket _browserSide, _hostSide;
    private readonly IPAddress _address;
    private readonly int _videoPt;
    private readonly Action<string> _log;
    private volatile IPEndPoint? _browser, _host;
    private volatile bool _closed;

    /// <summary>Percent of video RTP packets dropped on their way to the page.</summary>
    public volatile int LossToBrowser;
    private long _droppedToBrowser;
    public long DroppedToBrowser => Interlocked.Read(ref _droppedToBrowser);

    private LossyLink(IPAddress address, int videoPt, Action<string> log)
    {
        _address = address;
        _videoPt = videoPt;
        _log = log;
        _browserSide = Bind();
        _hostSide = Bind();
    }

    private static Socket Bind()
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Bind(new IPEndPoint(IPAddress.Any, 0));
        // Room for a 4K keyframe's burst of packets, so the relay itself loses none.
        s.ReceiveBufferSize = s.SendBufferSize = 8 << 20;
        // Don't let an ICMP port unreachable (a side that has closed) end the receive loop.
        const int SIO_UDP_CONNRESET = -1744830452;
        s.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
        return s;
    }

    /// <summary>
    /// Puts a relay in SIPSorcery's offer: its candidates are replaced by the relay's. Null (and
    /// the offer as it was) if the offer has no IPv4 host candidate to borrow an address from.
    /// </summary>
    public static (LossyLink? Link, string Sdp) InsertInOffer(string offer, int videoPt, Action<string> log)
    {
        var host = FirstHostCandidate(offer);
        if (host == null)
        {
            log("no IPv4 host candidate in SIPSorcery's offer: the leg goes direct, without loss simulation");
            return (null, offer);
        }
        var link = new LossyLink(host.Address, videoPt, log) { _host = host };
        link.Start();
        return (link, ReplaceCandidates(offer, host.Address, ((IPEndPoint)link._browserSide.LocalEndPoint!).Port));
    }

    /// <summary>The page's answer, its candidates replaced by the relay's; and the relay learns where the page is.</summary>
    public string InsertInAnswer(string answer)
    {
        _browser = FirstHostCandidate(answer);
        if (_browser == null) _log("no IPv4 host candidate in the page's answer: the relay will learn the page's address from its first packet");
        return ReplaceCandidates(answer, _address, ((IPEndPoint)_hostSide.LocalEndPoint!).Port);
    }

    private void Start()
    {
        new Thread(() => Pump(_browserSide, fromBrowser: true)) { IsBackground = true, Name = "link to host" }.Start();
        new Thread(() => Pump(_hostSide, fromBrowser: false)) { IsBackground = true, Name = "link to page" }.Start();
    }

    private void Pump(Socket from, bool fromBrowser)
    {
        var buffer = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!_closed)
        {
            int n;
            try
            {
                n = from.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException) { if (_closed) return; continue; }
            catch (ObjectDisposedException) { return; }

            var sender = (IPEndPoint)remote;
            IPEndPoint? to;
            if (fromBrowser)
            {
                if (_browser == null || !_browser.Equals(sender)) _browser = new IPEndPoint(sender.Address, sender.Port);
                to = _host;
            }
            else
            {
                to = _browser;
                if (IsVideoRtp(buffer, n) && Lose(LossToBrowser)) { Interlocked.Increment(ref _droppedToBrowser); continue; }
            }
            if (to == null) continue;
            try
            {
                (fromBrowser ? _hostSide : _browserSide).SendTo(buffer, 0, n, SocketFlags.None, to);
            }
            catch (SocketException) { /* the packet is lost, as it could be on a real network */ }
            catch (ObjectDisposedException) { return; }
        }
    }

    private static bool Lose(int percent) => percent > 0 && Random.Shared.Next(100) < percent;

    /// <summary>
    /// RTP (version 2, not an RTCP packet type 200 to 223, RFC 5761) with the video payload type.
    /// The header isn't encrypted by SRTP, so this works on the protected packets.
    /// </summary>
    private bool IsVideoRtp(byte[] b, int n) =>
        n >= 12 && (b[0] & 0xC0) == 0x80 && b[1] is not (>= 200 and <= 223) && (b[1] & 0x7F) == _videoPt;

    private static IPEndPoint? FirstHostCandidate(string sdp)
    {
        // a=candidate:<foundation> <component> <transport> <priority> <address> <port> typ <type> ...
        foreach (var t in sdp.Split('\n').Select(line => line.Trim().Split(' ')))
        {
            if (t.Length >= 8 && t[0].StartsWith("a=candidate:") && t[2].Equals("udp", StringComparison.OrdinalIgnoreCase) &&
                t[7] == "host" && IPAddress.TryParse(t[4], out var ip) && ip.AddressFamily == AddressFamily.InterNetwork &&
                int.TryParse(t[5], out int port))
            {
                return new IPEndPoint(ip, port);
            }
        }
        return null;
    }

    /// <summary>Each media section's candidates become the one given (rtcp-mux, so component 1 only).</summary>
    private static string ReplaceCandidates(string sdp, IPAddress address, int port)
    {
        var output = new List<string>();
        bool placed = false;
        foreach (var raw in sdp.Split("\r\n"))
        {
            if (raw.StartsWith("m=")) placed = false;
            if (raw.StartsWith("a=candidate:"))
            {
                if (!placed) output.Add($"a=candidate:1 1 udp 2130706431 {address} {port} typ host");
                placed = true;
                continue;
            }
            output.Add(raw);
        }
        return string.Join("\r\n", output);
    }

    public void Dispose()
    {
        _closed = true;
        _browserSide.Dispose();
        _hostSide.Dispose();
    }
}
