//-----------------------------------------------------------------------------
// Filename: RtpFecPayloadTypes.cs
// Description: Allocates and retains video RED/ULPFEC payload type bindings.
// License: BSD 3-Clause, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Keeps application codec capabilities separate from generated video redundancy
    /// formats, and reserves established FEC bindings for the session's lifetime.
    /// </summary>
    internal sealed class RtpFecPayloadTypes
    {
        private sealed class Pair
        {
            internal readonly SDPAudioVideoMediaFormat Red;
            internal readonly SDPAudioVideoMediaFormat Ulpfec;

            internal Pair(SDPAudioVideoMediaFormat red, SDPAudioVideoMediaFormat ulpfec)
            {
                Red = red;
                Ulpfec = ulpfec;
            }
        }

        private sealed class State
        {
            internal readonly Dictionary<int, SDPAudioVideoMediaFormat> History = new Dictionary<int, SDPAudioVideoMediaFormat>();
            internal Pair Preferred;
            internal Pair Offered;

            internal State Clone()
            {
                var clone = new State { Preferred = Preferred, Offered = Offered };
                foreach (var binding in History) { clone.History.Add(binding.Key, binding.Value); }
                return clone;
            }
        }

        private sealed class OfferTrack
        {
            internal MediaStream Stream;
            internal MediaStreamTrack Track;
            internal List<SDPAudioVideoMediaFormat> Formats;
            internal State State;
            internal bool Enabled;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<MediaStream, State> _states = new Dictionary<MediaStream, State>();
        private List<SDPAudioVideoMediaFormat> _negotiationScope = new List<SDPAudioVideoMediaFormat>();
        private bool _scopeBundled;

        /// <summary>
        /// Captures the incoming SDP namespace before negotiation changes local codec IDs.
        /// No auxiliary formats are allocated or advertised by this operation.
        /// </summary>
        internal void SetNegotiationScope(IReadOnlyList<SDPMediaAnnouncement> announcements, bool bundled)
        {
            if (announcements == null) { throw new ArgumentNullException(nameof(announcements)); }
            var formats = announcements
                // A zero port rejects this m-line; its advertised formats do not
                // occupy the live transport namespace. Inactive, non-zero-port
                // m-lines remain members and keep their bindings reserved.
                .Where(announcement => announcement.Port != 0 && IsRtpMedia(announcement.Media))
                .SelectMany(announcement => announcement.MediaFormats.Values)
                .ToList();
            lock (_lock)
            {
                _negotiationScope = formats;
                _scopeBundled = bundled;
            }
        }

        /// <summary>
        /// Prepares all offer capabilities transactionally. Existing codec IDs are never
        /// moved, and previously offered or negotiated FEC IDs cannot be repurposed.
        /// </summary>
        internal void PrepareOffer(IReadOnlyList<MediaStream> streams, bool bundled)
        {
            if (streams == null) { throw new ArgumentNullException(nameof(streams)); }
            lock (_lock)
            {
                var tracks = new List<OfferTrack>();
                foreach (var stream in streams.Where(stream => stream?.LocalTrack != null).Distinct())
                {
                    var track = stream.LocalTrack;
                    track.ValidateRedundancy();
                    bool video = track.Kind == SDPMediaTypesEnum.video;
                    var formats = GetOrdinaryCapabilities(track);
                    State state = null;
                    if (video)
                    {
                        state = _states.TryGetValue(stream, out var existing) ? existing.Clone() : new State();
                        state.Offered = null;
                    }
                    tracks.Add(new OfferTrack
                    {
                        Stream = stream,
                        Track = track,
                        Formats = formats,
                        State = state,
                        Enabled = video && IsEnabled(track) && formats.Any(format => format.IsVideoMediaCodec)
                    });
                }

                if (bundled)
                {
                    PrepareNamespace(tracks, _states.Values.SelectMany(state => state.History.Values));
                }
                else
                {
                    foreach (var track in tracks) { PrepareNamespace(new[] { track }); }
                }

                // Do not commit a partial offer if a track was replaced while preparing it.
                if (tracks.Any(track => !ReferenceEquals(track.Stream.LocalTrack, track.Track)))
                {
                    throw new InvalidOperationException("A media track changed while allocating video FEC payload types.");
                }
                foreach (var track in tracks.Where(track => track.State != null))
                {
                    track.Track.Capabilities = track.Formats;
                    _states[track.Stream] = track.State;
                }
                _negotiationScope = tracks.SelectMany(track => track.Formats).ToList();
                _scopeBundled = bundled;
            }
        }

        private static void PrepareNamespace(IReadOnlyList<OfferTrack> tracks,
            IEnumerable<SDPAudioVideoMediaFormat> previousBindings = null)
        {
            // Ordinary audio/text negotiation remains unchanged when this namespace has
            // never opted into video FEC.
            var history = previousBindings?.ToList() ?? new List<SDPAudioVideoMediaFormat>();
            if (history.Count == 0 && !tracks.Any(track => track.Enabled || (track.State?.History.Count ?? 0) > 0)) { return; }

            var bindings = new Dictionary<int, SDPAudioVideoMediaFormat>();
            foreach (var format in history) { AddBinding(bindings, format); }
            foreach (var track in tracks)
            {
                foreach (var format in track.Formats) { AddBinding(bindings, format); }
                if (track.State != null)
                {
                    foreach (var format in track.State.History.Values) { AddBinding(bindings, format); }
                }
            }

            foreach (var track in tracks.Where(track => track.Enabled))
            {
                var pair = track.State.Preferred;
                if (pair == null)
                {
                    var red = FindOrAllocate(bindings, true);
                    AddBinding(bindings, red);
                    var ulpfec = FindOrAllocate(bindings, false);
                    AddBinding(bindings, ulpfec);
                    pair = new Pair(red, ulpfec);
                }
                else
                {
                    AddBinding(bindings, pair.Red);
                    AddBinding(bindings, pair.Ulpfec);
                }

                track.Formats.Add(pair.Red);
                track.Formats.Add(pair.Ulpfec);
                track.State.Preferred = pair;
                track.State.Offered = pair;
                AddBinding(track.State.History, pair.Red);
                AddBinding(track.State.History, pair.Ulpfec);
            }
        }

        private static SDPAudioVideoMediaFormat FindOrAllocate(Dictionary<int, SDPAudioVideoMediaFormat> bindings, bool red)
        {
            var expected = red
                ? SDPAudioVideoMediaFormat.CreateRedFormat(SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN)
                : SDPAudioVideoMediaFormat.CreateUlpfecFormat(SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN);
            foreach (var binding in bindings.OrderBy(binding => binding.Key))
            {
                if (IsDynamic(binding.Key) && SameDefinition(binding.Value, expected)) { return binding.Value; }
            }
            for (int id = SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN; id <= SDPAudioVideoMediaFormat.DYNAMIC_ID_MAX; id++)
            {
                if (!bindings.ContainsKey(id))
                {
                    return red ? SDPAudioVideoMediaFormat.CreateRedFormat(id) : SDPAudioVideoMediaFormat.CreateUlpfecFormat(id);
                }
            }
            throw new InvalidOperationException("Video FEC requires a free RED/ULPFEC payload type pair in the dynamic range 96-127.");
        }

        /// <summary>
        /// Returns temporary local matching capabilities. Offers use the peer's complete
        /// pair; answers may only retain the exact auxiliary pair we actually offered.
        /// </summary>
        internal List<SDPAudioVideoMediaFormat> GetNegotiationCapabilities(MediaStream stream,
            List<SDPAudioVideoMediaFormat> remoteFormats, bool remoteOffer)
        {
            if (stream == null) { throw new ArgumentNullException(nameof(stream)); }
            var track = stream.LocalTrack;
            if (track == null) { return new List<SDPAudioVideoMediaFormat>(); }
            track.ValidateRedundancy();
            var formats = GetOrdinaryCapabilities(track);
            if (track.Kind != SDPMediaTypesEnum.video || !IsEnabled(track) || remoteFormats == null ||
                !SDPAudioVideoMediaFormat.GetCompatibleFormats(remoteFormats, formats).Any(format => format.IsVideoMediaCodec))
            {
                return formats;
            }

            lock (_lock)
            {
                Pair pair;
                if (remoteOffer)
                {
                    if (!TryGetPair(remoteFormats, out pair)) { return formats; }
                }
                else
                {
                    if (!_states.TryGetValue(stream, out var state) || state.Offered == null) { return formats; }
                    pair = state.Offered;
                    if (!remoteFormats.Any(format => SameBinding(format, pair.Red)) ||
                        !remoteFormats.Any(format => SameBinding(format, pair.Ulpfec)))
                    {
                        return formats;
                    }
                }
                formats.Add(pair.Red);
                formats.Add(pair.Ulpfec);
                return formats;
            }
        }

        /// <summary>
        /// Retains successfully negotiated peer bindings without changing the requested
        /// track policy. A plain-video fallback leaves all earlier FEC bindings reserved.
        /// </summary>
        internal void RecordNegotiatedFormats(MediaStream stream, List<SDPAudioVideoMediaFormat> formats, bool bundled)
        {
            if (stream == null) { throw new ArgumentNullException(nameof(stream)); }
            var track = stream.LocalTrack;
            if (track == null || track.Kind != SDPMediaTypesEnum.video) { return; }
            track.ValidateRedundancy();
            if (!IsEnabled(track) || formats == null || !formats.Any(format => format.IsVideoMediaCodec) ||
                !TryGetPair(formats, out var pair)) { return; }

            lock (_lock)
            {
                var bindings = new Dictionary<int, SDPAudioVideoMediaFormat>();
                // The remote namespace is authoritative here: its primary media IDs may
                // differ from the local IDs still present before intersection is committed.
                foreach (var format in bundled && _scopeBundled ? _negotiationScope : formats)
                {
                    AddBinding(bindings, format);
                }
                foreach (var state in _states.Where(item => bundled || ReferenceEquals(item.Key, stream)).Select(item => item.Value))
                {
                    foreach (var binding in state.History.Values) { AddBinding(bindings, binding); }
                }
                AddBinding(bindings, pair.Red);
                AddBinding(bindings, pair.Ulpfec);

                var updated = _states.TryGetValue(stream, out var current) ? current.Clone() : new State();
                AddBinding(updated.History, pair.Red);
                AddBinding(updated.History, pair.Ulpfec);
                updated.Preferred = pair;
                _states[stream] = updated;
            }
        }

        private static bool IsEnabled(MediaStreamTrack track) => track.Redundancy == (RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec);

        private static bool IsRtpMedia(SDPMediaTypesEnum media) => media == SDPMediaTypesEnum.audio ||
            media == SDPMediaTypesEnum.video || media == SDPMediaTypesEnum.text;

        private static List<SDPAudioVideoMediaFormat> GetOrdinaryCapabilities(MediaStreamTrack track)
            => (track.Capabilities ?? new List<SDPAudioVideoMediaFormat>())
                .Where(format => track.Kind != SDPMediaTypesEnum.video || (!format.IsRed && !format.IsUlpfec))
                .ToList();

        private static bool TryGetPair(IReadOnlyList<SDPAudioVideoMediaFormat> formats, out Pair pair)
        {
            pair = null;
            SDPAudioVideoMediaFormat? red = null;
            SDPAudioVideoMediaFormat? ulpfec = null;
            foreach (var format in formats)
            {
                if (format.Kind != SDPMediaTypesEnum.video) { continue; }
                if (format.IsRed && SDPAudioVideoMediaFormat.AreMatch(format, SDPAudioVideoMediaFormat.CreateRedFormat(format.ID)))
                {
                    red ??= format;
                }
                else if (format.IsUlpfec && SDPAudioVideoMediaFormat.AreMatch(format, SDPAudioVideoMediaFormat.CreateUlpfecFormat(format.ID)))
                {
                    ulpfec ??= format;
                }
            }
            if (!red.HasValue || !ulpfec.HasValue || red.Value.ID == ulpfec.Value.ID) { return false; }
            pair = new Pair(red.Value, ulpfec.Value);
            return true;
        }

        private static bool IsDynamic(int id) => id >= SDPAudioVideoMediaFormat.DYNAMIC_ID_MIN && id <= SDPAudioVideoMediaFormat.DYNAMIC_ID_MAX;

        private static bool SameDefinition(SDPAudioVideoMediaFormat first, SDPAudioVideoMediaFormat second)
            => first.Kind == second.Kind &&
                string.Equals(first.Rtpmap?.Trim(), second.Rtpmap?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(first.Fmtp?.Trim() ?? string.Empty, second.Fmtp?.Trim() ?? string.Empty, StringComparison.Ordinal);

        private static bool SameBinding(SDPAudioVideoMediaFormat first, SDPAudioVideoMediaFormat second)
            => first.ID == second.ID && SameDefinition(first, second);

        private static void AddBinding(Dictionary<int, SDPAudioVideoMediaFormat> bindings, SDPAudioVideoMediaFormat format)
        {
            if (bindings.TryGetValue(format.ID, out var existing))
            {
                if (!SameDefinition(existing, format))
                {
                    throw new InvalidOperationException($"RTP payload type {format.ID} conflicts with an established codec/FEC binding ({existing.Rtpmap} versus {format.Rtpmap}).");
                }
                return;
            }
            bindings.Add(format.ID, format);
        }
    }
}
