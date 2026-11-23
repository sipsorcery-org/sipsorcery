//-----------------------------------------------------------------------------
// Filename: SIPUserAgentRtpTimeoutUnitTest.cs
//
// Description: Unit tests for how the SIPUserAgent handles an RTP inactivity
// timeout on the call's media session: the OnRtpTimeout event and the
// HangupOnRtpTimeout setting.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 05 Nov 2026  Claude Code - Opus 5.5  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserAgentRtpTimeoutUnitTest
    {
        private readonly ILogger logger;

        public SIPUserAgentRtpTimeoutUnitTest(ITestOutputHelper output)
        {
            logger = TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// By default an audio RTP timeout hangs up the call and the event is fired.
        /// </summary>
        [Fact]
        public async Task AudioTimeout_DefaultSettings_RaisesEventAndHangsUp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6091);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                var (_, mediaSession) = await SIPUserAgentSingleCallUnitTest.EstablishDialogAsync(agent, channel, channelEndPoint);

                SDPMediaTypesEnum? timedOutMedia = null;
                agent.OnRtpTimeout += (mediaType) => timedOutMedia = mediaType;

                mediaSession.RaiseTimeout(SDPMediaTypesEnum.audio);

                Assert.Equal(SDPMediaTypesEnum.audio, timedOutMedia);
                Assert.False(agent.IsCallActive, "The call should have been hung up.");
                Assert.True(mediaSession.IsClosed);
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// With HangupOnRtpTimeout disabled the event is fired and the call is left up for
        /// the application to deal with.
        /// </summary>
        [Fact]
        public async Task AudioTimeout_HangupDisabled_RaisesEventAndKeepsCall()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6092);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);
            agent.HangupOnRtpTimeout = false;

            try
            {
                var (_, mediaSession) = await SIPUserAgentSingleCallUnitTest.EstablishDialogAsync(agent, channel, channelEndPoint);

                SDPMediaTypesEnum? timedOutMedia = null;
                agent.OnRtpTimeout += (mediaType) => timedOutMedia = mediaType;

                mediaSession.RaiseTimeout(SDPMediaTypesEnum.audio);

                Assert.Equal(SDPMediaTypesEnum.audio, timedOutMedia);
                Assert.True(agent.IsCallActive, "The call should not have been hung up.");
                Assert.False(mediaSession.IsClosed);
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }

        /// <summary>
        /// An exception thrown by an application's event handler must not stop the agent
        /// from applying its default hangup behaviour.
        /// </summary>
        [Fact]
        public async Task AudioTimeout_HandlerThrows_StillHangsUp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channelEndPoint = new IPEndPoint(IPAddress.Loopback, 6093);
            var channel = new RecordingMockSIPChannel(channelEndPoint);
            var transport = new SIPTransport();
            transport.AddSIPChannel(channel);

            var agent = new SIPUserAgent(transport, null);

            try
            {
                var (_, mediaSession) = await SIPUserAgentSingleCallUnitTest.EstablishDialogAsync(agent, channel, channelEndPoint);

                agent.OnRtpTimeout += (mediaType) => throw new System.ApplicationException("handler failure");

                mediaSession.RaiseTimeout(SDPMediaTypesEnum.audio);

                Assert.False(agent.IsCallActive, "The call should have been hung up.");
            }
            finally
            {
                agent.Close();
                transport.Shutdown();
            }
        }
    }
}
