//-----------------------------------------------------------------------------
// Filename: BrowserStatsReport.cs
//
// Description: The browser's own view of a WebRTC video session, as the
// "webrtc browser-test" page reports it back to the CLI.
//
// This is the half of an interop test the CLI cannot see for itself. Our
// receive-side counters (RTP packets, sequence gaps, depacketised frames) say
// what arrived and what we made of it; they cannot say what Chrome THOUGHT it
// sent, nor whether Chrome's decoder was happy with what we sent back. So the
// page polls pc.getStats(), flattens the one outbound-rtp and one inbound-rtp
// video report (resolving codecId to a mimeType on the way, which getStats
// only exposes indirectly) and POSTs the result here.
//
// Read together the two sides localise a fault that neither side can localise
// alone. The signature worth knowing: Chrome's pliCount or freezeCount
// climbing while our VideoLost stays at 0 means every packet arrived and we
// still could not hand the decoder a usable frame - a packetisation or
// reassembly bug, not a network one.
//
// Parsing is deliberately total: every field is optional because the stats a
// browser exposes vary by browser, version and codec, and a missing counter
// must leave a null in the result rather than fail the run. Only malformed
// JSON is an error.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text.Json;

namespace SIPSorcery.Cli.Commands;

/// <summary>
/// One flattened RTCStats video report (outbound-rtp or inbound-rtp) as the browser page sends it.
/// Every member is nullable: which counters a browser publishes varies by browser, version and
/// codec, and an absent counter has to read as "not reported" rather than as zero.
/// </summary>
public sealed record BrowserVideoStats(
    string? Codec = null,
    int? Width = null,
    int? Height = null,
    double? FramesPerSecond = null,
    int? FramesEncoded = null,
    int? FramesSent = null,
    int? FramesReceived = null,
    int? FramesDecoded = null,
    int? FramesDropped = null,
    int? KeyFramesEncoded = null,
    int? KeyFramesDecoded = null,
    int? FreezeCount = null,
    int? PliCount = null,
    int? NackCount = null,
    int? FirCount = null,
    long? PacketsSent = null,
    long? PacketsReceived = null,
    long? PacketsLost = null,
    long? BytesSent = null,
    long? BytesReceived = null,
    string? QualityLimitationReason = null);

/// <summary>
/// One stats POST from the browser page. <see cref="Final"/> marks the report the page sends as it
/// stops, which is the one the verb reports: the earlier ones are progress.
/// </summary>
public sealed record BrowserStatsReport(
    string? Mode = null,
    int? ElapsedMs = null,
    bool Final = false,
    string? Error = null,
    BrowserVideoStats? Outbound = null,
    BrowserVideoStats? Inbound = null,
    // Identifies the page load the report came from, so reports from a tab whose offer was refused
    // can be ignored. Without it a rejected tab's "final" error report would end the live session.
    string? SessionId = null)
{
    /// <summary>The largest stats body accepted, a guard against an unbounded POST on the listener.</summary>
    public const int MAX_BODY_BYTES = 64 * 1024;

    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses a stats POST body. Unknown members are ignored and missing ones stay null, so a
    /// browser that reports more or fewer counters than Chrome still parses; only JSON that is not
    /// a readable object is an error.
    /// </summary>
    public static bool TryParse(string? json, out BrowserStatsReport? report, out string? error)
    {
        report = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The browser stats POST had an empty body.";
            return false;
        }

        try
        {
            report = JsonSerializer.Deserialize<BrowserStatsReport>(json, _options);
        }
        catch (JsonException excp)
        {
            error = $"The browser stats POST was not valid JSON: {excp.Message}";
            return false;
        }

        if (report == null)
        {
            error = "The browser stats POST deserialised to null.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// A one line summary of the browser's side for the human readable result, picking whichever
    /// direction the mode makes meaningful. Returns null when the browser never reported.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>();

        if (Outbound != null)
        {
            parts.Add($"sent {Outbound.FramesEncoded?.ToString() ?? "?"} encoded / {Outbound.PacketsSent?.ToString() ?? "?"} packets" +
                      $" as {Outbound.Codec ?? "?"}{FormatQualityLimit(Outbound.QualityLimitationReason)}");
        }

        if (Inbound != null)
        {
            parts.Add($"decoded {Inbound.FramesDecoded?.ToString() ?? "?"} frames" +
                      $" ({Inbound.KeyFramesDecoded?.ToString() ?? "?"} key, {Inbound.FramesDropped?.ToString() ?? "?"} dropped," +
                      $" {Inbound.FreezeCount?.ToString() ?? "?"} freezes, {Inbound.PacketsLost?.ToString() ?? "?"} lost)");
        }

        return parts.Count == 0 ? "no video reports" : string.Join("; ", parts);
    }

    private static string FormatQualityLimit(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason == "none" ? string.Empty : $" (quality limited: {reason})";
}
