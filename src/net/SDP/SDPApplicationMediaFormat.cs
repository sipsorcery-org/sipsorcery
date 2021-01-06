//-----------------------------------------------------------------------------
// Filename: SDPApplicationMediaFormat.cs
//
// Description: An SDP media format for an "application" media announcement.
// These media formats differ from those used with "audio" and "video"
// announcements.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 OCt 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net
{
    public struct SDPApplicationMediaFormat
    {
        public string ID;

        public string Rtpmap;

        public string Fmtp;

        public SDPApplicationMediaFormat(string id)
        {
            ID = id;
            Rtpmap = null;
            Fmtp = null;
        }

        public SDPApplicationMediaFormat(string id, string rtpmap, string fmtp)
        {
            ID = id;
            Rtpmap = rtpmap;
            Fmtp = fmtp;
        }

        /// <summary>
        /// Creates a new media format based on an existing format but with a different ID.
        /// The typical case for this is during the SDP offer/answer exchange the dynamic format ID's for the
        /// equivalent type need to be adjusted by one party.
        /// </summary>
        /// <param name="id">The ID to set on the new format.</param>
        public SDPApplicationMediaFormat WithUpdatedID(string id) =>
            new SDPApplicationMediaFormat(id, Rtpmap, Fmtp);

        public SDPApplicationMediaFormat WithUpdatedRtpmap(string rtpmap) =>
            new SDPApplicationMediaFormat(ID, rtpmap, Fmtp);

        public SDPApplicationMediaFormat WithUpdatedFmtp(string fmtp) =>
            new SDPApplicationMediaFormat(ID, Rtpmap, fmtp);
    }
}
