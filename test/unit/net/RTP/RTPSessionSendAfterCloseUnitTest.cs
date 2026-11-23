//-----------------------------------------------------------------------------
// Filename: RTPSessionSendAfterCloseUnitTest.cs
//
// Description: A media source runs on its own thread and cannot be stopped
// synchronously with the session closing, so a short tail of sends after Close
// is expected even from correct application code. Those sends must be dropped
// quietly: throwing would surface on the source's thread during an orderly
// shutdown, and warning on each one turns a clean shutdown into a burst of log
// noise.
//
// Sends still arriving well after the close are a different thing, the source
// was never stopped, so past a grace period they are reported as a warning,
// rate limited.
//
// History:
// 19 Sep 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionSendAfterCloseUnitTest
    {
        /// <summary>
        /// Comfortably more sends than the interval at which the drop path consults the clock, so
        /// a test does not depend on that interval's exact value.
        /// </summary>
        private const int SENDS_PAST_CLOCK_CHECK = 100;

        private readonly ILogger logger;

        public RTPSessionSendAfterCloseUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// The shutdown race. A media source still producing samples when the session closes must
        /// not be able to throw out of the send call.
        /// </summary>
        [Fact]
        public void SendAudioAfterClose_IsDroppedWithoutThrowing()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.Close("normal");

                Assert.True(session.IsClosed);

                // A few samples, as a real source would emit over the interval between the close
                // and the source actually stopping.
                for (int i = 0; i < 5; i++)
                {
                    session.SendAudio(160, new byte[160]);
                }
            }
        }

        /// <summary>
        /// The same for video, which reaches the send path through a different stream class.
        /// </summary>
        [Fact]
        public void SendVideoAfterClose_IsDroppedWithoutThrowing()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithVideoTrack().Build())
            {
                session.Close("normal");

                session.SendVideo(3000, new byte[256]);
            }
        }

        /// <summary>
        /// Closing twice must not change the outcome. Applications commonly close on a state change
        /// and again on dispose.
        /// </summary>
        [Fact]
        public void SendAudioAfterRepeatedClose_IsDroppedWithoutThrowing()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.Close("normal");
                session.Close("normal");

                session.SendAudio(160, new byte[160]);
            }
        }

        /// <summary>
        /// The expected tail. However many samples a source emits while it is winding down, none of
        /// them warn as long as they arrive inside the grace period.
        /// </summary>
        [Fact]
        public void SendsWithinGracePeriod_DoNotWarn()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.Close("normal");

                SendAudioPackets(session, SENDS_PAST_CLOCK_CHECK);

                Assert.Equal(SENDS_PAST_CLOCK_CHECK, GetSendsAfterClose(session));
                Assert.Equal(0, GetLastWarningTicks(session));
            }
        }

        /// <summary>
        /// A source that was never stopped. Once sends are still arriving past the grace period the
        /// application has to be told, so a warning is raised.
        /// </summary>
        [Fact]
        public void SendsAfterGracePeriod_Warn()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.Close("normal");
                BackdateClose(session, MediaStream.CLOSED_SEND_GRACE_PERIOD_MS + 1000);

                SendAudioPackets(session, SENDS_PAST_CLOCK_CHECK);

                Assert.NotEqual(0, GetLastWarningTicks(session));
            }
        }

        /// <summary>
        /// The warning must not become the per packet noise it replaced. Once one has been raised
        /// the rest are suppressed until the interval has passed, and then one more is allowed.
        /// </summary>
        [Fact]
        public void WarningsAfterGracePeriod_AreRateLimited()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.Close("normal");
                BackdateClose(session, MediaStream.CLOSED_SEND_GRACE_PERIOD_MS + 1000);

                SendAudioPackets(session, SENDS_PAST_CLOCK_CHECK);
                long firstWarningTicks = GetLastWarningTicks(session);
                Assert.NotEqual(0, firstWarningTicks);

                // Still inside the warning interval, so nothing more is reported.
                SendAudioPackets(session, SENDS_PAST_CLOCK_CHECK);
                Assert.Equal(firstWarningTicks, GetLastWarningTicks(session));

                // Once the interval has passed the next batch reports again.
                BackdateLastWarning(session, MediaStream.CLOSED_SEND_WARNING_INTERVAL_MS + 1000);
                SendAudioPackets(session, SENDS_PAST_CLOCK_CHECK);

                Assert.NotEqual(firstWarningTicks, GetLastWarningTicks(session));
            }
        }

        /// <summary>
        /// An open session carries none of this state, and closing is what starts the clock.
        /// </summary>
        [Fact]
        public void OpenSession_HasNoDropState()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                Assert.Equal(0, GetClosedAtTicks(session));
                Assert.Equal(0, GetSendsAfterClose(session));

                session.Close("normal");

                Assert.NotEqual(0, GetClosedAtTicks(session));
                Assert.Equal(0, GetSendsAfterClose(session));
            }
        }

        // ---------- helpers ----------

        private static void SendAudioPackets(RTPSession session, int count)
        {
            for (int i = 0; i < count; i++)
            {
                session.SendAudio(160, new byte[160]);
            }
        }

        /// <summary>
        /// Moves the recorded close time into the past so a test can reach the behaviour that
        /// applies after the grace period without waiting for it in real time.
        /// </summary>
        private static void BackdateClose(RTPSession session, int milliseconds)
        {
            SetLongField(session, "_closedAtTicks",
                GetClosedAtTicks(session) - milliseconds * TimeSpan.TicksPerMillisecond);
        }

        private static void BackdateLastWarning(RTPSession session, int milliseconds)
        {
            SetLongField(session, "_lastClosedSendWarningTicks",
                GetLastWarningTicks(session) - milliseconds * TimeSpan.TicksPerMillisecond);
        }

        private static long GetClosedAtTicks(RTPSession session) => GetLongField(session, "_closedAtTicks");

        private static long GetSendsAfterClose(RTPSession session) => GetLongField(session, "_sendsAfterClose");

        private static long GetLastWarningTicks(RTPSession session) => GetLongField(session, "_lastClosedSendWarningTicks");

        /// <summary>
        /// Reaches into the audio MediaStream's private drop accounting. None of it is exposed
        /// publicly, and asserting on it is the stable way to test the reporting decisions without
        /// swapping out the global log factory, which would race with every other test class.
        /// </summary>
        private static FieldInfo GetField(string name)
        {
            var field = typeof(MediaStream).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field;
        }

        private static long GetLongField(RTPSession session, string name) => (long)GetField(name).GetValue(session.AudioStream);

        private static void SetLongField(RTPSession session, string name, long value) => GetField(name).SetValue(session.AudioStream, value);
    }
}
