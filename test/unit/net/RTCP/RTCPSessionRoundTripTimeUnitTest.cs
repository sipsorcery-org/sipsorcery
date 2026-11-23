//-----------------------------------------------------------------------------
// Filename: RTCPSessionRoundTripTimeUnitTest.cs
//
// Description: Unit tests for the round trip time an RTCP session measures
// from the reception reports it receives.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Xunit;

namespace SIPSorcery.Net.UnitTests;

[Trait("Category", "unit")]
public class RTCPSessionRoundTripTimeUnitTest
{
    private const uint LOCAL_SSRC = 0x22222222;
    private const double NTP_TIMESTAMP32_UNITS_PER_SECOND = 65536;
    private static readonly TimeSpan SenderReportAge = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ReceiverDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TruncationTolerance = TimeSpan.FromMilliseconds(1);

    private static ReceptionReportSample ReceptionReport(uint ssrc, uint lastSenderReport, uint delaySinceLastSenderReport) =>
        new(ssrc, 0, 0, 0, 0, lastSenderReport, delaySinceLastSenderReport);

    /// <summary>
    /// The round trip time is the time since the sender report a reception report for our SSRC echoes, less
    /// the time the remote party held it (RFC 3550 section 6.4.1). Reports for other SSRCs, or that echo no
    /// sender report, are ignored.
    /// </summary>
    [Fact]
    public void MeasuresRoundTripTimeFromReceptionReports()
    {
        var session = new RTCPSession(SDPMediaTypesEnum.video, LOCAL_SSRC);
        var start = DateTime.Now;
        uint lastSenderReport = RTCPSession.DateTimeToNtpTimestamp32(start - SenderReportAge);
        uint delay = (uint)(ReceiverDelay.TotalSeconds * NTP_TIMESTAMP32_UNITS_PER_SECOND);

        session.UpdateRoundTripTime(ReceptionReport(LOCAL_SSRC + 1, lastSenderReport, delay));
        session.UpdateRoundTripTime(ReceptionReport(LOCAL_SSRC, 0, 0));
        Assert.Equal(TimeSpan.Zero, session.RoundTripTime);

        session.UpdateRoundTripTime(ReceptionReport(LOCAL_SSRC, lastSenderReport, delay));
        var elapsed = DateTime.Now - start;
        Assert.InRange(session.RoundTripTime, SenderReportAge - ReceiverDelay - TruncationTolerance, SenderReportAge - ReceiverDelay + elapsed + TruncationTolerance);
    }
}
