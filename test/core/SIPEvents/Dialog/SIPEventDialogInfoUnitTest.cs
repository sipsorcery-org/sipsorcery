//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//using System.Xml;
//using System.Xml.Linq;
//using System.Xml.Schema;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPEventDialogInfoUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPEventDialogInfoUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static string m_dialogXMLNS = SIPEventDialogInfo.m_dialogXMLNS;
        //private static XmlSchemaSet m_eventDialogSchema;

        /// <summary>
        /// Used to check the conformance of blocks of XML text to the schema in RFC 4235.
        /// </summary>
        //[Fact]
        //[Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
        //[ExpectedException(typeof(XmlSchemaValidationException))]
        /// Commented out due to excluding xsd resources files that were breaking the WSL build. AC 14 Nov 2019
        //public void InvalidXMLUnitTest()
        //{
        //    logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    if (m_eventDialogSchema == null)
        //    {
        //        logger.LogDebug("Loading XSD schema for dialog event package, takes a while...");

        //        m_eventDialogSchema = new XmlSchemaSet();
        //        XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.EventDialogSchema, XmlNodeType.Document, null);
        //        m_eventDialogSchema.Add(m_dialogXMLNS, schemaReader);
        //    }

        //    // The mandatory version attribute on dialog-info is missing.
        //    string invalidDialogInfoXMLStr =
        //         "<?xml version='1.0' encoding='utf-16'?>" +
        //         "<dialog-info state='full' entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:dialog-info'>" +
        //         " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
        //         "  <state event='rejected' code='486'>terminated</state>" +
        //         " </dialog>" +
        //         "</dialog-info>";

        //    XDocument eventDialogDoc = XDocument.Parse(invalidDialogInfoXMLStr);
        //    eventDialogDoc.Validate(m_eventDialogSchema, (o, e) =>
        //    {
        //        logger.LogDebug("XSD validation " + e.Severity + " event: " + e.Message);

        //        if (e.Severity == XmlSeverityType.Error)
        //        {
        //            throw e.Exception;
        //        }
        //    });

        //    logger.LogDebug("-----------------------------------------");
        //}

        /// <summary>
        /// Used to check the conformance of blocks of XML text to the schema in RFC 4235.
        /// </summary>
        /// Commented out due to excluding xsd resources files that were breaking the WSL build. AC 14 Nov 2019
        //[Fact]
        //[Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
        //public void ValidXMLUnitTest()
        //{
        //    logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    if (m_eventDialogSchema == null)
        //    {
        //        logger.LogDebug("Loading XSD schema for dialog event package, takes a while...");

        //        m_eventDialogSchema = new XmlSchemaSet();
        //        XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.EventDialogSchema, XmlNodeType.Document, null);
        //        m_eventDialogSchema.Add(m_dialogXMLNS, schemaReader);
        //    }

        //    string validDialogInfoXMLStr =
        //        "<?xml version='1.0' encoding='utf-16'?>" +
        //         "<dialog-info version='1' state='full' entity='sip:test@test.com'" +
        //         "  xmlns='urn:ietf:params:xml:ns:dialog-info' xmlns:ss='sipsorcery:dialog-info'>" +
        //         " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
        //         "  <state event='remote-bye' code='486'>terminated</state>" +
        //         "  <duration>13</duration>" +
        //         "  <local>" +
        //         "   <identity>sip:109@sipsorcery.com;user=phone</identity>" +
        //         "   <cseq>2</cseq>" +
        //         "  </local>" +
        //         "  <remote>" +
        //         "   <identity display-name='Joe Bloggs'>sip:thisis@anonymous.invalid</identity>" +
        //         "   <target uri='sip:user@10.1.1.7:5070' />" +
        //         "   <cseq>1</cseq>" +
        //         "   <ss:sdp/>" +
        //         "  </remote>" +
        //         " </dialog>" +
        //         "</dialog-info>";

        //    XDocument eventDialogDoc = XDocument.Parse(validDialogInfoXMLStr);
        //    eventDialogDoc.Validate(m_eventDialogSchema, (o, e) =>
        //    {
        //        logger.LogDebug("XSD validation " + e.Severity + " event: " + e.Message);

        //        if (e.Severity == XmlSeverityType.Error)
        //        {
        //            throw e.Exception;
        //        }
        //    });

        //    logger.LogDebug("-----------------------------------------");
        //}

        /// <summary>
        /// Tests that a SIPEventDialogInfo will generate an XML text representation of itself without throwing any exceptions.
        /// </summary>
        [Fact]
        public void GetAsXMLStringUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPEventDialogInfo dialogInfo = new SIPEventDialogInfo(0, SIPEventDialogInfoStateEnum.full, SIPURI.ParseSIPURI("sip:test@test.com"));
            dialogInfo.DialogItems.Add(new SIPEventDialog("abcde", "terminated", 487, SIPEventDialogStateEvent.Cancelled, 2));

            logger.LogDebug(dialogInfo.ToXMLText());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a single dialog block of XML text is correctly parsed and the value of each individual item is correctly extracted.
        /// </summary>
        [Fact]
        public void ParseFromXMLStringUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string eventDialogInfoStr = "<?xml version='1.0' encoding='utf-16'?>" +
                 "<dialog-info version='1' state='full' entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:dialog-info'>" +
                 " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
                 "  <state event='remote-bye' code='486'>terminated</state>" +
                 "  <duration>13</duration>" +
                 " </dialog>" +
                 "</dialog-info>";

            SIPEventDialogInfo dialogInfo = SIPEventDialogInfo.Parse(eventDialogInfoStr);

            Assert.True(dialogInfo.Version == 1, "The parsed event dialog version was incorrect.");
            Assert.True(dialogInfo.State == SIPEventDialogInfoStateEnum.full, "The parsed event dialog state was incorrect.");
            Assert.True(dialogInfo.Entity == SIPURI.ParseSIPURI("sip:test@test.com"), "The parsed event dialog entity was incorrect.");
            Assert.True(dialogInfo.DialogItems.Count == 1, "The parsed event dialog items count was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].ID == "as7d900as8", "The parsed event dialog event id was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].CallID == "a84b4c76e66710", "The parsed event dialog event call-id was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].LocalTag == "1928301774", "The parsed event dialog event local-tag was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].Direction == SIPEventDialogDirectionEnum.initiator, "The parsed event dialog event direction was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].State == "terminated", "The parsed event dialog event state was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].StateEvent == SIPEventDialogStateEvent.RemoteBye, "The parsed event dialog event state event was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].StateCode == 486, "The parsed event dialog event state code was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].Duration == 13, "The parsed event dialog event duration was incorrect.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a dialog-info block of XML text with multiple child dialogs is correctly parsed and the value of the critical pieces
        /// of information is correctly extracted.
        /// </summary>
        [Fact]
        public void ParseFromXMLStringMultiDialogsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string eventDialogInfoStr = "<?xml version='1.0' encoding='utf-16'?>" +
                 "<dialog-info version='1' state='full' entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:dialog-info'>" +
                 " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
                 "  <state event='remote-bye' code='486'>terminated</state>" +
                 "  <duration>13</duration>" +
                 " </dialog>" +
                 " <dialog id='4353458'>" +
                 "  <state>progressing</state>" +
                 " </dialog>" +
                 "</dialog-info>";

            SIPEventDialogInfo dialogInfo = SIPEventDialogInfo.Parse(eventDialogInfoStr);

            Assert.True(dialogInfo.DialogItems.Count == 2, "The parsed event dialog items count was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].ID == "as7d900as8", "The parsed event dialog event id for the first dialog was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].State == "terminated", "The parsed event dialog event state for the first dialog was incorrect.");
            Assert.True(dialogInfo.DialogItems[1].ID == "4353458", "The parsed event dialog event id for the second dialog was incorrect.");
            Assert.True(dialogInfo.DialogItems[1].State == "progressing", "The parsed event dialog event state for the second dialog was incorrect.");

            logger.LogDebug(dialogInfo.ToXMLText());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that a dialog-info block of XML text with a child dialog containing participants is correctly parsed 
        /// and the value of the critical pieces of information is correctly extracted.
        /// </summary>
        [Fact]
        public void ParseFromXMLStringDialogWithParticipantsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string eventDialogInfoStr = "<?xml version='1.0' encoding='utf-16'?>" +
                 "<dialog-info version='1' state='full' entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:dialog-info'>" +
                 " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
                 "  <state event='remote-bye' code='486'>terminated</state>" +
                 "  <duration>13</duration>" +
                 "  <local>" +
                 "   <identity>sip:109@sipsorcery.com;user=phone</identity>" +
                 "   <cseq>2</cseq>" +
                 "  </local>" +
                 "  <remote>" +
                 "   <identity display-name='Joe Bloggs'>sip:thisis@anonymous.invalid</identity>" +
                 "   <target uri='sip:user@10.1.1.7:5070' />" +
                 "   <cseq>1</cseq>" +
                 "  </remote>" +
                 " </dialog>" +
                 "</dialog-info>";

            SIPEventDialogInfo dialogInfo = SIPEventDialogInfo.Parse(eventDialogInfoStr);

            Assert.True(dialogInfo.DialogItems[0].LocalParticipant != null, "The parsed event dialog local participant was not correct.");
            Assert.True(dialogInfo.DialogItems[0].RemoteParticipant != null, "The parsed event dialog remote participant was not correct.");
            Assert.True(dialogInfo.DialogItems[0].LocalParticipant.URI == SIPURI.ParseSIPURI("sip:109@sipsorcery.com;user=phone"), "The local participant URI was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].LocalParticipant.CSeq == 2, "The local participant CSeq was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].RemoteParticipant.URI == SIPURI.ParseSIPURI("sip:thisis@anonymous.invalid"), "The remote participant URI was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].RemoteParticipant.DisplayName == "Joe Bloggs", "The remote participant display name was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].RemoteParticipant.TargetURI == SIPURI.ParseSIPURI("sip:user@10.1.1.7:5070"), "The remote participant target URI was incorrect.");
            Assert.True(dialogInfo.DialogItems[0].RemoteParticipant.CSeq == 1, "The remote participant CSeq was incorrect.");

            logger.LogDebug(dialogInfo.ToXMLText());

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests that the data in the SDP nodes is correctly parsed.
        /// </summary>
        /*[Fact]
        public void ParseSDPFromXMLStringDialogUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string CRLF = "\r\n";
            string sdp =
               "v=0" + CRLF +
                "o=- " + Crypto.GetRandomInt(1000, 5000).ToString() + " 2 IN IP4 10.1.1.1" + CRLF +
                "s=session" + CRLF +
                "c=IN IP4 10.1.1.1" + CRLF +
                "t=0 0" + CRLF +
                "m=audio 14000 RTP/AVP 0 18 101" + CRLF +
                "a=rtpmap:0 PCMU/8000" + CRLF +
                "a=rtpmap:18 G729/8000" + CRLF +
                "a=rtpmap:101 telephone-event/8000" + CRLF +
                "a=fmtp:101 0-16" + CRLF +
                "a=recvonly";

            string eventDialogInfoStr = "<?xml version='1.0' encoding='utf-16'?>" +
                 "<dialog-info version='1' state='full' entity='sip:test@test.com' " +
                 " xmlns='urn:ietf:params:xml:ns:dialog-info' xmlns:ss='sipsorcery:dialog-info'>" +
                 " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
                 "  <state event='remote-bye' code='486'>terminated</state>" +
                 "  <duration>13</duration>" +
                 "  <local>" +
                 "   <identity>sip:109@sipsorcery.com;user=phone</identity>" +
                 "   <cseq>2</cseq>" +
                 "   <ss:sdp>" + sdp + "</ss:sdp>" +
                 "  </local>" +
                 "  <remote>" +
                 "   <identity display-name='Joe Bloggs'>sip:thisis@anonymous.invalid</identity>" +
                 "   <target uri='sip:user@10.1.1.7:5070' />" +
                 "   <cseq>1</cseq>" +
                 "   <ss:sdp>" + sdp + "</ss:sdp>" +
                 "  </remote>" +
                 " </dialog>" +
                 "</dialog-info>";

            SIPEventDialogInfo dialogInfo = SIPEventDialogInfo.Parse(eventDialogInfoStr);

            Assert.NotNull(dialogInfo.DialogItems[0].LocalParticipant, "The parsed event dialog local participant was not correct.");
            Assert.NotNull(dialogInfo.DialogItems[0].RemoteParticipant, "The parsed event dialog remote participant was not correct.");
            Assert.True(dialogInfo.DialogItems[0].LocalParticipant.SDP == sdp, "The local participant SDP was parsed incorrectly.");
            Assert.True(dialogInfo.DialogItems[0].RemoteParticipant.SDP == sdp, "The remote participant SDP was parsed incorrectly.");

            logger.LogDebug(dialogInfo.ToXMLText());

            logger.LogDebug("-----------------------------------------");
        }*/
    }
}
