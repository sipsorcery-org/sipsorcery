using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>This class contains unit tests for SDPMediaAnnouncement</summary>
    [Trait("Category", "unit")]
    public class SDPMediaAnnouncementUnitTests
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SDPMediaAnnouncementUnitTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }
        /// <summary>
        /// Checks that the SDP with Message Media is well formatted.
        /// </summary>
        [Fact]
        public void InvalidPortInRemoteOfferTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var remoteOffer = new SDP();

            var sessionPort = 5523;
            var sessionEndpoint = "10vMB2Ee;tcp";

            remoteOffer.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            var messageMediaFormat = new SDPMessageMediaFormat();
            messageMediaFormat.IP = remoteOffer.Connection.ConnectionAddress;
            messageMediaFormat.Port = sessionPort.ToString();
            messageMediaFormat.Endpoint = sessionEndpoint;
            messageMediaFormat.AcceptTypes = new List<string>
            {
                "text/plain",
                "text/x-msrp-heartbeat"
            };

            SDPMediaAnnouncement messageAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.message,
                remoteOffer.Connection,
                sessionPort,
                messageMediaFormat);

            messageAnnouncement.Transport = "TCP/MSRP";

            remoteOffer.Media.Add(messageAnnouncement);

            var sdpOffer = remoteOffer.ToString();
            var msrpMediaAttribute =
                $"{SDPMediaAnnouncement.MEDIA_FORMAT_PATH_MSRP_PREFIX}//{remoteOffer.Connection.ConnectionAddress}:{sessionPort}/{sessionEndpoint}";
            var msrpMediaTypes = $"{SDPMediaAnnouncement.MEDIA_FORMAT_PATH_ACCEPT_TYPES_PREFIX}text/plain text/x-msrp-heartbeat";
            var mediaDescription = $"m=message {sessionPort} TCP/MSRP *";
            Assert.Contains(msrpMediaAttribute, sdpOffer);
            Assert.Contains(msrpMediaTypes, sdpOffer);
            Assert.Contains(mediaDescription, sdpOffer);
        }
    }
}
