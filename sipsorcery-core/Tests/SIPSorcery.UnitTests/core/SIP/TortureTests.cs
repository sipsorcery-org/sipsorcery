using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.SIP;

namespace SIPSorcery.SIP.Core.UnitTests
{
    /// <summary>
    /// Torture tests from RFC??
    /// </summary>
    [TestClass]
    public class SIPTortureTests
    {
        private TestContext testContextInstance;
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        public SIPTortureTests()
        { }

        public void TestMethod1()
        {
             Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipMsg =
                    @"INVITE sip:vivekg@chair-dnrc.example.com;unknownparam SIP/2.0
        TO :
        sip:vivekg@chair-dnrc.example.com ;   tag    = 1918181833n
        from   : ""J Rosenberg \\\""""       <sip:jdrosen@example.com>
        ;
        tag = 98asjd8
        MaX-fOrWaRdS: 0068
        Call-ID: wsinv.ndaksdj@192.0.2.1
        Content-Length   : 150
        cseq: 0009
        INVITE
        Via  : SIP  /   2.0
        /UDP
        192.0.2.2;branch=390skdjuw
        s :
        NewFangledHeader:   newfangled value
        continued newfangled value
        UnknownHeaderWithUnusualValue: ;;,,;;,;
        Content-Type: application/sdp
        Route:
        <sip:services.example.com;lr;unknownwith=value;unknown-no-value>
        v:  SIP  / 2.0  / TCP     spindle.example.com   ;
        branch  =   z9hG4bK9ikj8  ,
        SIP  /    2.0   / UDP  192.168.255.111   ; branch=
        z9hG4bK30239
        m:""Quoted string \""\"""" <sip:jdrosen@example.com> ; newparam =
        newvalue ;
        secondparam ; q = 0.33

        v=0
      o=mhandley 29739 7272939 IN IP4 192.0.2.3
      s=-
      c=IN IP4 192.0.2.4
      t=0 0
      m=audio 49217 RTP/AVP 0 12
      m=video 3227 RTP/AVP 31
      a=rtpmap:31 LPC";

                SIPMessage sipMessage = SIPMessage.ParseSIPMessage(Encoding.UTF8.GetBytes(sipMsg), null, null);
                SIPRequest inviteReq = SIPRequest.ParseSIPRequest(sipMessage);

                Console.WriteLine(inviteReq.ToString());

                Console.WriteLine("-----------------------------------------");
        }
    }
}
