using SIPSorcery.Net;
using SIPSorcery.SIP;

namespace SIPSorcery.Softphone;

/// <summary>
/// A call's offers and answers as they went over the wire, for the Streams tab in Diagnostics:
/// every INVITE and UPDATE, their responses, and an ACK that carries SDP. Phone adds the
/// messages as the transport traces them, and <see cref="Build"/> pairs them up. It says who
/// offered, why (a hold, a resume, video added, a transfer's new session), what each side's SDP
/// said, and the streams as the last answer left them. A transfer keeps the same call, so one
/// log can span several Call-IDs (sessions).
/// It also keeps what the Connection tab needs: the SIP messages that set the call up and end it
/// (<see cref="View.Sip"/>), the media layers' events as they happen (ICE, DTLS, SRTP, RTP, RTCP:
/// <see cref="AddEvent"/>), and their latest facts (<see cref="Set"/>).
/// And for the SIP ladder tab, every SIP message of the call as it crossed the wire (<see cref="AddWire"/>):
/// each sending its own entry, a retransmission numbered as one.
/// </summary>
public sealed class StreamLog
{
    /// <summary>One SIP message of an offer/answer. Status is 0 for a request.</summary>
    public sealed record Message(DateTime At, bool Out, string CallId, int CSeq, string Method, int Status, string? Reason,
        string? Sdp, bool Replaces, bool ReferredBy, string? Who);

    /// <summary>
    /// One offer/answer. ByUs: we made the offer. OfferMsg and AnswerMsg name the messages
    /// ("re-INVITE", "200 OK", "ACK"). Status is the final response ("488 Not Acceptable Here"),
    /// or the latest provisional one while it's still going. Session counts the Call-IDs that got
    /// an answer: 2 after a transfer.
    /// </summary>
    public sealed record Exchange(int N, DateTime At, bool ByUs, string Label, string OfferMsg, string? AnswerMsg,
        string? Status, bool Ok, bool Pending, int? Session, string? Offer, string? Answer, int? AnswerMs);

    /// <summary>A media line as the last answer left it: each side's port and direction (sendrecv, sendonly, recvonly, inactive).</summary>
    public sealed record Stream(int Index, string Kind, int OurPort, int TheirPort, string Transport, string Security,
        string Ours, string Theirs, string[] Formats);

    /// <summary>Something that happened to the media that no SDP says: an RTP timeout, an RTCP BYE.</summary>
    public sealed record Note(DateTime At, string Text, string Level);

    /// <summary>A SIP message, for the Connection tab's timeline: "INVITE", "180 Ringing", "ACK", "BYE".</summary>
    public sealed record SipEvent(DateTime At, bool Out, string Text, int CSeq);

    /// <summary>One of the media layers' events: Layer is ice, dtls, srtp, rtp or rtcp; Level info, ok, warn or bad.</summary>
    public sealed record Event(DateTime At, string Layer, string Text, string Level);

    /// <summary>
    /// One SIP message as it crossed the wire, for the SIP ladder: our socket and theirs, what it was
    /// (Label "INVITE" or "180 Ringing"), the Request-URI and From of a request, and its whole text.
    /// Attempt is which sending of it this is: 2 for the first retransmission.
    /// </summary>
    public sealed record Wire(DateTime At, bool Out, string Local, string Remote, string CallId, int CSeq, string CSeqMethod,
        string Label, string? Uri, string? From, bool Response, string Branch, int Attempt, string Text);

    public sealed record View(IReadOnlyList<Exchange> Exchanges, IReadOnlyList<Stream> Streams, IReadOnlyList<Note> Notes,
        IReadOnlyList<SipEvent> Sip, IReadOnlyList<Event> Events, IReadOnlyDictionary<string, object?> Facts, IReadOnlyList<Wire> Wire);

    /// <summary>The most a call's ladder keeps: a call that's up for hours re-INVITEs now and then.</summary>
    private const int WireMax = 500;

    private readonly object _lock = new();
    private readonly List<Message> _messages = new();
    private readonly HashSet<(bool, string, int, string, int)> _seen = new();
    private readonly List<Note> _notes = new();
    private readonly List<Event> _events = new();
    private readonly Dictionary<string, object?> _facts = new();
    private readonly List<Wire> _wire = new();
    private readonly Dictionary<(bool, string, int, string, string, string), int> _sendings = new();

    /// <summary>The message as one for this log, or null if it has no part in an offer/answer.</summary>
    public static Message? From(SIPRequest req, bool sent)
    {
        // BYE and CANCEL, and an ACK without SDP, aren't offers or answers, but the Connection
        // tab's timeline shows them.
        if (req.Method is not (SIPMethodsEnum.INVITE or SIPMethodsEnum.ACK or SIPMethodsEnum.UPDATE or SIPMethodsEnum.BYE or SIPMethodsEnum.CANCEL)) return null;
        var sdp = SdpOf(req.Header.ContentType, req.Body);
        // Who it's to (ours) or from (theirs), for "Replaced by Carol" and "Transfer: called Dave".
        var who = sent ? req.Header.To?.ToName is { Length: > 0 } tn ? tn : req.Header.To?.ToURI?.User
            : req.Header.From?.FromName is { Length: > 0 } fn ? fn : req.Header.From?.FromURI?.User;
        return new Message(DateTime.Now, sent, req.Header.CallId, req.Header.CSeq, req.Method.ToString(), 0, null, sdp,
            !string.IsNullOrWhiteSpace(req.Header.Replaces), !string.IsNullOrWhiteSpace(req.Header.ReferredBy), who);
    }

    public static Message? From(SIPResponse resp, bool sent)
    {
        if (resp.Header.CSeqMethod is not (SIPMethodsEnum.INVITE or SIPMethodsEnum.UPDATE) || resp.StatusCode < 101) return null;
        return new Message(DateTime.Now, sent, resp.Header.CallId, resp.Header.CSeq, resp.Header.CSeqMethod.ToString(), resp.StatusCode,
            resp.ReasonPhrase, SdpOf(resp.Header.ContentType, resp.Body), false, false, null);
    }

    /// <summary>The message as the ladder draws it (its Attempt set as it's added).</summary>
    public static Wire WireOf(SIPEndPoint local, SIPEndPoint remote, SIPMessageBase msg, bool sent)
    {
        var h = msg.Header;
        var (label, uri, from, cseqMethod) = msg switch
        {
            SIPRequest q => (q.Method.ToString(), q.URI?.ToString(), h.From is { } f
                ? (string.IsNullOrWhiteSpace(f.FromName) ? f.FromURI?.ToParameterlessString() : $"{f.FromName} <{f.FromURI?.ToParameterlessString()}>")
                : null, q.Method.ToString()),
            SIPResponse r => ($"{r.StatusCode} {r.ReasonPhrase}", null, null, h.CSeqMethod.ToString()),
            _ => ("?", null, null, ""),
        };
        return new Wire(DateTime.Now, sent, local?.ToString() ?? "", remote?.ToString() ?? "", h.CallId ?? "", h.CSeq, cseqMethod,
            label, uri, from, msg is SIPResponse, h.Vias?.TopViaHeader?.Branch ?? "", 1, msg.ToString() ?? "");
    }

    /// <summary>
    /// Adds a message to the ladder. A retransmission is its own entry, and says which sending it is:
    /// the same message (Call-ID, CSeq, what it is, the top Via's branch) going the same way again.
    /// </summary>
    /// <summary>How many messages the ladder has.</summary>
    public int WireCount { get { lock (_lock) return _wire.Count; } }

    public void AddWire(Wire w)
    {
        lock (_lock)
        {
            if (_wire.Count >= WireMax) return;
            var key = (w.Out, w.CallId, w.CSeq, w.CSeqMethod, w.Label, w.Branch);
            int attempt = _sendings.GetValueOrDefault(key) + 1;
            _sendings[key] = attempt;
            _wire.Add(w with { Attempt = attempt });
        }
    }

    private static string? SdpOf(string? contentType, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        if (contentType?.Contains("multipart", StringComparison.OrdinalIgnoreCase) == true) return null;
        return contentType?.Contains("sdp", StringComparison.OrdinalIgnoreCase) == true || body.TrimStart().StartsWith("v=0") ? body : null;
    }

    /// <summary>Adds the message; false if it's a retransmission of one already here.</summary>
    public bool Add(Message m)
    {
        lock (_lock)
        {
            if (!_seen.Add((m.Out, m.CallId, m.CSeq, m.Method, m.Status))) return false;
            _messages.Add(m);
            return true;
        }
    }

    public void AddNote(string text, string level = "info")
    {
        lock (_lock) _notes.Add(new Note(DateTime.Now, text, level));
    }

    /// <summary>A media layer's event (ice, dtls, srtp, rtp, rtcp), for the Connection tab.</summary>
    public void AddEvent(string layer, string text, string level = "info")
    {
        lock (_lock) _events.Add(new Event(DateTime.Now, layer, text, level));
    }

    /// <summary>A fact the Connection tab shows (iceState, dtlsRole, rtcpRtt, …): a string, number or bool.</summary>
    public void Set(string key, object? value)
    {
        lock (_lock) _facts[key] = value;
    }

    /// <summary>A fact, as it is now.</summary>
    public object? Get(string key)
    {
        lock (_lock) return _facts.GetValueOrDefault(key);
    }

    /// <summary>Adds to a count among the facts (srtpErrors, say), and returns it.</summary>
    public long Count(string key, long by = 1)
    {
        lock (_lock)
        {
            long n = (_facts.GetValueOrDefault(key) as long? ?? 0) + by;
            _facts[key] = n;
            return n;
        }
    }

    /// <summary>A time for the page, as its Date.now() counts.</summary>
    public static long PageTime(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

    // A media line's facts, from one side's SDP.
    private sealed record Line(string Kind, int Port, string Dir, string Transport, bool Crypto, string[] Formats);

    private static List<Line> Lines(string? sdp)
    {
        var lines = new List<Line>();
        if (sdp == null) return lines;
        try
        {
            foreach (var m in SDP.ParseSDPDescription(sdp).Media)
            {
                var kind = m.Media == SDPMediaTypesEnum.application ? "data" : m.Media.ToString();
                var formats = m.Media == SDPMediaTypesEnum.application
                    ? m.ApplicationMediaFormats.Values.Select(f => f.ID).ToArray()
                    : m.MediaFormats.Values.Select(Format).ToArray();
                lines.Add(new Line(kind, m.Port, Direction(m.MediaStreamStatus), m.Transport, m.SecurityDescriptions.Count > 0, formats));
            }
        }
        catch { }
        return lines;
    }

    private static string Format(SDPAudioVideoMediaFormat f)
    {
        try { return $"{f.Name()}/{f.ClockRate()}"; } catch { return f.ID.ToString(); }
    }

    private static string Direction(MediaStreamStatusEnum? s) => (s ?? MediaStreamStatusEnum.SendRecv) switch
    {
        MediaStreamStatusEnum.SendOnly => "sendonly",
        MediaStreamStatusEnum.RecvOnly => "recvonly",
        MediaStreamStatusEnum.Inactive => "inactive",
        _ => "sendrecv",
    };

    private static bool Holding(string dir) => dir is "sendonly" or "inactive";

    // Each kind's first live line (port not 0): its direction.
    private static Dictionary<string, string> DirsByKind(List<Line> lines)
    {
        var d = new Dictionary<string, string>();
        foreach (var l in lines) if (l.Port != 0) d.TryAdd(l.Kind, l.Dir);
        return d;
    }

    private static string KindName(string kind) => kind == "data" ? "text (data channel)" : kind;

    /// <summary>The offers and answers so far, the streams as the last answer left them, and the notes.</summary>
    public View Build()
    {
        List<Message> all;
        List<Note> notes;
        List<Event> events;
        Dictionary<string, object?> facts;
        List<Wire> wire;
        lock (_lock) { all = _messages.ToList(); notes = _notes.ToList(); events = _events.ToList(); facts = new(_facts); wire = _wire.ToList(); }
        // The SIP lane: every message, in order (offer/answer or not).
        var sip = all.Select(m => new SipEvent(m.At, m.Out, m.Status > 0 ? $"{m.Status} {m.Reason}" : m.Method, m.CSeq)).ToList();
        // The offers and answers: no BYE or CANCEL, and an ACK only with SDP.
        var msgs = all.Where(m => m.Method is "INVITE" or "UPDATE" || (m.Method == "ACK" && m.Sdp != null)).ToList();
        var exchanges = new List<Exchange>();
        var answeredCallIds = new List<string>();
        Dictionary<string, string>? prevOurs = null, prevTheirs = null;   // per kind, from the last answered exchange
        (List<Line> Ours, List<Line> Theirs, bool ByUs)? last = null;
        foreach (var r in msgs.Where(m => m.Status == 0 && m.Method is "INVITE" or "UPDATE"))
        {
            var resps = msgs.Where(m => m.Status > 0 && m.CallId == r.CallId && m.CSeq == r.CSeq && m.Method == r.Method && m.Out != r.Out).ToList();
            var final = resps.FirstOrDefault(m => m.Status >= 200);
            // Challenged for credentials: it goes again, with them, as a new request.
            if (final?.Status is 401 or 407) continue;
            bool inDialog = answeredCallIds.Contains(r.CallId);
            string requestName = r.Method == "INVITE" && inDialog ? "re-INVITE" : r.Method;
            string? offer, answer, answerMsg;
            string offerMsg;
            bool byUs;
            DateTime? answerAt;
            if (r.Sdp != null)
            {
                (offer, byUs, offerMsg) = (r.Sdp, r.Out, requestName);
                var a = resps.FirstOrDefault(m => m.Sdp != null && m.Status < 300);
                (answer, answerMsg, answerAt) = (a?.Sdp, a != null ? $"{a.Status} {a.Reason}" : null, a?.At);
            }
            else
            {
                // No offer in the request: it comes in their 200 OK, and the answer in the ACK.
                var o = resps.FirstOrDefault(m => m.Sdp != null && m.Status is >= 200 and < 300);
                var ack = msgs.FirstOrDefault(m => m.Method == "ACK" && m.CallId == r.CallId && m.CSeq == r.CSeq && m.Out == r.Out);
                (offer, byUs, offerMsg) = (o?.Sdp, o != null ? !r.Out : r.Out, o != null ? $"{o.Status} {o.Reason}" : requestName);
                (answer, answerMsg, answerAt) = (ack?.Sdp, ack != null ? "ACK" : null, ack?.At);
                if (o != null) offerMsg = $"{requestName} without SDP, then {offerMsg}";
            }
            bool ok = final is { Status: >= 200 and < 300 } && answer != null;
            if (ok && !answeredCallIds.Contains(r.CallId)) answeredCallIds.Add(r.CallId);
            var status = final != null ? $"{final.Status} {final.Reason}" : resps.LastOrDefault() is { } p ? $"{p.Status} {p.Reason}" : null;

            var offerLines = Lines(offer);
            var offerDirs = DirsByKind(offerLines);
            string label;
            if (!inDialog)
            {
                label = exchanges.Count == 0 ? "Call"
                    : prevOurs == null ? "Call again"
                    : r.Replaces ? $"Replaced by {r.Who ?? "them"}"
                    : r.Out && r.ReferredBy ? $"Transfer: called {r.Who ?? "them"}"
                    : "New session";
            }
            else
            {
                var offererPrev = (byUs ? prevOurs : prevTheirs) ?? new();
                var added = offerDirs.Keys.Where(k => !offererPrev.ContainsKey(k)).ToList();
                var dropped = offererPrev.Keys.Where(k => !offerDirs.ContainsKey(k)).ToList();
                var key = offerDirs.ContainsKey("audio") ? "audio" : offerDirs.Keys.FirstOrDefault();
                bool holdNow = key != null && Holding(offerDirs[key]);
                bool holdBefore = key != null && offererPrev.TryGetValue(key, out var pd) && Holding(pd);
                label = added.Count > 0 ? (byUs ? "Added " : "They added ") + string.Join(" and ", added.Select(KindName))
                    : dropped.Count > 0 ? (byUs ? "Dropped " : "They dropped ") + string.Join(" and ", dropped.Select(KindName))
                    : holdNow && !holdBefore ? (byUs ? "Hold" : "Held by them")
                    : !holdNow && holdBefore ? (byUs ? "Resume" : "They resumed")
                    : byUs ? $"{requestName}, no change" : $"{requestName} from them, no change";
            }
            if (ok)
            {
                var answerLines = Lines(answer);
                var ours = byUs ? offerLines : answerLines;
                var theirs = byUs ? answerLines : offerLines;
                prevOurs = DirsByKind(ours);
                prevTheirs = DirsByKind(theirs);
                last = (ours, theirs, byUs);
            }
            int idx = answeredCallIds.IndexOf(r.CallId);
            exchanges.Add(new Exchange(exchanges.Count + 1, r.At, byUs, label, offerMsg, answerMsg, status, ok,
                Pending: final == null, Session: idx >= 0 ? idx + 1 : null, offer, answer,
                answerAt is { } at ? (int)(at - r.At).TotalMilliseconds : null));
        }

        var streams = new List<Stream>();
        if (last is { } l)
        {
            for (int i = 0; i < Math.Max(l.Ours.Count, l.Theirs.Count); i++)
            {
                var a = i < l.Ours.Count ? l.Ours[i] : null;
                var b = i < l.Theirs.Count ? l.Theirs[i] : null;
                var any = a ?? b!;
                var transport = b?.Transport ?? any.Transport;
                var security = transport.Contains("TLS", StringComparison.OrdinalIgnoreCase) ? (transport.Contains("SCTP", StringComparison.OrdinalIgnoreCase) ? "DTLS" : "DTLS-SRTP")
                    : a?.Crypto == true && b?.Crypto == true ? "SRTP (SDES)" : "None";
                // The answer's formats are the ones agreed: theirs if we offered, ours if they did.
                var answerLine = l.ByUs ? b : a;
                var formats = (answerLine?.Formats.Length > 0 ? answerLine.Formats : any.Formats) ?? Array.Empty<string>();
                streams.Add(new Stream(i, any.Kind, a?.Port ?? 0, b?.Port ?? 0, transport, security, a?.Dir ?? "inactive", b?.Dir ?? "inactive", formats));
            }
        }
        return new View(exchanges, streams, notes, sip, events, facts, wire);
    }
}
