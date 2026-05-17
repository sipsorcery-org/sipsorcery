using System.Collections;
using NUnit.Framework;
using SIPSorcery.Net;
using UnityEngine.TestTools;

namespace SipSorcery.Tests
{
    /// <summary>
    /// Regression coverage for https://github.com/sipsorcery-org/sipsorcery/issues/1614.
    ///
    /// On Unity's Mono runtime, NetworkInformation.NetworkChange throws
    /// PlatformNotSupportedException. The SIPSorcery.Sys.NetServices static
    /// constructor used to subscribe unconditionally, which made constructing
    /// any RTCPeerConnection fail with a TypeInitializationException. This
    /// test passes once the subscription is guarded.
    /// </summary>
    public class SipSorcerySmokeTests
    {
        [UnityTest]
        public IEnumerator CanConstructRtcPeerConnectionOnUnityRuntime()
        {
            RTCPeerConnection pc = null;
            Assert.DoesNotThrow(() => pc = new RTCPeerConnection());
            Assert.IsNotNull(pc);
            pc.Close("test complete");
            yield return null;
        }
    }
}
