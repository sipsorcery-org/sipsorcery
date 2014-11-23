// ============================================================================
// FileName: SIPEventDialogInfo.cs
//
// Description:
// Represents the top level XML element on a SIP event dialog payload as described in: 
// RFC4235 "An INVITE-Initiated Dialog Event Package for the Session Initiation Protocol (SIP)".
//
// Author(s):
// Aaron Clauson
//
// History:
// 28 Feb 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// RFC4235 on Dialog Event Packages:
    ///  - To estalish a subscription to a specific dialog the call-id, to-tag and from-tag must be specified,
    ///  - To establish a subscription to a set of dialogs the call-id and to-tag must be specified.
    ///  Treatment of the Event header:
    ///   - If the Event header contains dialog identifiers a notification is sent for any dialogs that match them AND the user in the SUBSCRIBE URI.
    ///   - If the Event header does not contain any dialog identifiers then a notification is sent for every dialog that matches the user in the SUBSCRIBE URI.
    /// - Notifications contain the identities of the dialog participants, the target URIs and the dialog identifiers.
    /// - The format of the NOTIFY bodies must be in a format specified in a SUBSCRIBE Accept header or if omitted a default format of "application/dialog-info+xml".
    /// 
    /// Example of an empty dialog notification body:
    ///  <?xml version="1.0"?>
    ///  <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="0" notify-state="full" entity="sip:alice@example.com" />
    /// 
    /// Example of a single entry dialog notification body:
    /// <?xml version="1.0"?>
    /// <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="0" state="partial" entity="sip:alice@example.com">
    ///   <dialog id="as7d900as8" call-id="a84b4c76e66710" local-tag="1928301774" direction="initiator">
    ///    <state event="rejected" code="486">terminated</state> <!-- The state element is the only mandatory child element for a dialog element. -->
    ///    <duration>145</duration>
    ///   </dialog>
    ///  </dialog-info>
    /// 
    /// </remarks>
    public class SIPEventDialogInfo : SIPEvent
    {
        private static ILog logger = AppState.logger;

        private static readonly string m_dialogXMLNS = SIPEventConsts.DIALOG_XML_NAMESPACE_URN;
        //private static readonly string m_sipsorceryXMLNS = SIPEventConsts.SIPSORCERY_DIALOG_XML_NAMESPACE_URN;
        //private static readonly string m_sipsorceryXMLPrefix = SIPEventConsts.SIPSORCERY_DIALOG_XML_NAMESPACE_PREFIX;

        public int Version;
        public SIPEventDialogInfoStateEnum State;
        public SIPURI Entity;
        public List<SIPEventDialog> DialogItems = new List<SIPEventDialog>();

        public SIPEventDialogInfo()
        { }

        public SIPEventDialogInfo(int version, SIPEventDialogInfoStateEnum state, SIPURI entity)
        {
            Version = version;
            State = state;
            Entity = entity.CopyOf();
        }

        public override void Load(string dialogInfoXMLStr)
        {
            try
            {
                XNamespace ns = m_dialogXMLNS;
                XDocument eventDialogDoc = XDocument.Parse(dialogInfoXMLStr);

                Version = Convert.ToInt32(((XElement)eventDialogDoc.FirstNode).Attribute("version").Value);
                State = (SIPEventDialogInfoStateEnum)Enum.Parse(typeof(SIPEventDialogInfoStateEnum), ((XElement)eventDialogDoc.FirstNode).Attribute("state").Value, true);
                Entity = SIPURI.ParseSIPURI(((XElement)eventDialogDoc.FirstNode).Attribute("entity").Value);

                var dialogElements = eventDialogDoc.Root.Elements(ns + "dialog");
                foreach (XElement dialogElement in dialogElements)
                {
                    DialogItems.Add(SIPEventDialog.Parse(dialogElement));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPEventDialogInfo Ctor. " + excp.Message);
                throw;
            }
        }

        public static SIPEventDialogInfo Parse(string dialogInfoXMLStr)
        {
           SIPEventDialogInfo eventDialogInfo = new SIPEventDialogInfo();
           eventDialogInfo.Load(dialogInfoXMLStr);
           return eventDialogInfo;
        }

        public override string ToXMLText()
        {
            XNamespace ns = m_dialogXMLNS;
            //XNamespace ss = m_sipsorceryXMLNS;
            XDocument dialogEventDoc = new XDocument(new XElement(ns + "dialog-info",
                //new XAttribute(XNamespace.Xmlns + m_sipsorceryXMLPrefix, m_sipsorceryXMLNS),
                new XAttribute("version", Version),
                new XAttribute("state", State),
                new XAttribute("entity", Entity.ToString())
                ));

            DialogItems.ForEach((item) =>
            {
                XElement dialogItemElement = item.ToXML();
                dialogEventDoc.Root.Add(dialogItemElement);
                item.HasBeenSent = true;
            });

            StringBuilder sb = new StringBuilder();
            XmlWriterSettings xws = new XmlWriterSettings();
            xws.NewLineHandling = NewLineHandling.None;
            xws.Indent = true;

            using (XmlWriter xw = XmlWriter.Create(sb, xws))
            {
                dialogEventDoc.WriteTo(xw);
            }

            return sb.ToString();
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
        public class SIPDialogEventInfoUnitTest
        {
            private static XmlSchemaSet m_eventDialogSchema;

            [TestFixtureSetUp]
            public void Init()
            {
                log4net.Config.BasicConfigurator.Configure();
            }

            /// <summary>
            /// Used to check the conformance of blocks of XML text to the schema in RFC 4235.
            /// </summary>
            [Test]
            [Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
            [ExpectedException(typeof(XmlSchemaValidationException))]
            public void InvalidXMLUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                if (m_eventDialogSchema == null)
                {
                    Console.WriteLine("Loading XSD schema for dialog event package, takes a while...");

                    m_eventDialogSchema = new XmlSchemaSet();
                    XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.EventDialogSchema, XmlNodeType.Document, null);
                    m_eventDialogSchema.Add(m_dialogXMLNS, schemaReader);
                }

                // The mandatory version attribue on dialog-info is missing.
                string invalidDialogInfoXMLStr = 
                     "<?xml version='1.0' encoding='utf-16'?>" +
                     "<dialog-info state='full' entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:dialog-info'>" +
                     " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
                     "  <state event='rejected' code='486'>terminated</state>" +
                     " </dialog>" +
                     "</dialog-info>";

                XDocument eventDialogDoc = XDocument.Parse(invalidDialogInfoXMLStr);
                eventDialogDoc.Validate(m_eventDialogSchema, (o, e) =>
                {
                    Console.WriteLine("XSD validation " + e.Severity + " event: " + e.Message);

                    if (e.Severity == XmlSeverityType.Error)
                    {
                        throw e.Exception;
                    }
                });

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Used to check the conformance of blocks of XML text to the schema in RFC 4235.
            /// </summary>
            [Test]
            [Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
            public void ValidXMLUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                if (m_eventDialogSchema == null)
                {
                    Console.WriteLine("Loading XSD schema for dialog event package, takes a while...");

                    m_eventDialogSchema = new XmlSchemaSet();
                    XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.EventDialogSchema, XmlNodeType.Document, null);
                    m_eventDialogSchema.Add(m_dialogXMLNS, schemaReader);
                }

                string validDialogInfoXMLStr =
                    "<?xml version='1.0' encoding='utf-16'?>" +
                     "<dialog-info version='1' state='full' entity='sip:test@test.com'" + 
                     "  xmlns='urn:ietf:params:xml:ns:dialog-info' xmlns:ss='sipsorcery:dialog-info'>" +
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
                     "   <ss:sdp/>" +
                     "  </remote>" +
                     " </dialog>" +
                     "</dialog-info>";

                XDocument eventDialogDoc = XDocument.Parse(validDialogInfoXMLStr);
                eventDialogDoc.Validate(m_eventDialogSchema, (o, e) =>
                {
                    Console.WriteLine("XSD validation " + e.Severity + " event: " + e.Message);

                    if (e.Severity == XmlSeverityType.Error)
                    {
                        throw e.Exception;
                    }
                });

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Tests that a SIPEventDialogInfo will generate an XML text representation of itself without throwing any exceptions.
            /// </summary>
            [Test]
            public void GetAsXMLStringUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPEventDialogInfo dialogInfo = new SIPEventDialogInfo(0, SIPEventDialogInfoStateEnum.full, SIPURI.ParseSIPURI("sip:test@test.com"));
                dialogInfo.DialogItems.Add(new SIPEventDialog("abcde", "terminated", 487, SIPEventDialogStateEvent.Cancelled, 2));

                Console.WriteLine(dialogInfo.ToXMLText());

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Tests that a single dialog block of XML text is correctly parsed and the value of each individual item is correctly extracted.
            /// </summary>
            [Test]
            public void ParseFromXMLStringUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string eventDialogInfoStr = "<?xml version='1.0' encoding='utf-16'?>" +
                     "<dialog-info version='1' state='full' entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:dialog-info'>" +
                     " <dialog id='as7d900as8' call-id='a84b4c76e66710' local-tag='1928301774' direction='initiator'>" +
                     "  <state event='remote-bye' code='486'>terminated</state>" +
                     "  <duration>13</duration>" +
                     " </dialog>" +
                     "</dialog-info>";

                SIPEventDialogInfo dialogInfo = SIPEventDialogInfo.Parse(eventDialogInfoStr);

                Assert.IsTrue(dialogInfo.Version == 1, "The parsed event dialog version was incorrect.");
                Assert.IsTrue(dialogInfo.State == SIPEventDialogInfoStateEnum.full, "The parsed event dialog state was incorrect.");
                Assert.IsTrue(dialogInfo.Entity == SIPURI.ParseSIPURI("sip:test@test.com"), "The parsed event dialog entity was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems.Count == 1, "The parsed event dialog items count was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].ID == "as7d900as8", "The parsed event dialog event id was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].CallID == "a84b4c76e66710", "The parsed event dialog event call-id was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].LocalTag == "1928301774", "The parsed event dialog event local-tag was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].Direction == SIPEventDialogDirectionEnum.initiator, "The parsed event dialog event direction was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].State == "terminated", "The parsed event dialog event state was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].StateEvent == SIPEventDialogStateEvent.RemoteBye, "The parsed event dialog event state event was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].StateCode == 486, "The parsed event dialog event state code was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].Duration == 13, "The parsed event dialog event duration was incorrect.");

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Tests that a dialog-info block of XML text with multiple child dialogs is correctly parsed and the value of the critical pieces
            /// of information is correctly extracted.
            /// </summary>
            [Test]
            public void ParseFromXMLStringMultiDialogsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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

                Assert.IsTrue(dialogInfo.DialogItems.Count == 2, "The parsed event dialog items count was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].ID == "as7d900as8", "The parsed event dialog event id for the first dialog was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].State == "terminated", "The parsed event dialog event state for the first dialog was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[1].ID == "4353458", "The parsed event dialog event id for the second dialog was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[1].State == "progressing", "The parsed event dialog event state for the second dialog was incorrect.");

                Console.WriteLine(dialogInfo.ToXMLText());

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Tests that a dialog-info block of XML text with a child dialog containting participants is correctly parsed 
            /// and the value of the critical pieces of information is correctly extracted.
            /// </summary>
            [Test]
            public void ParseFromXMLStringDialogWithParticipantsUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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

                Assert.NotNull(dialogInfo.DialogItems[0].LocalParticipant, "The parsed event dialog local participant was not correct.");
                Assert.NotNull(dialogInfo.DialogItems[0].RemoteParticipant, "The parsed event dialog remote participant was not correct.");
                Assert.IsTrue(dialogInfo.DialogItems[0].LocalParticipant.URI == SIPURI.ParseSIPURI("sip:109@sipsorcery.com;user=phone"), "The local participant URI was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].LocalParticipant.CSeq == 2, "The local participant CSeq was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].RemoteParticipant.URI == SIPURI.ParseSIPURI("sip:thisis@anonymous.invalid"), "The remote participant URI was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].RemoteParticipant.DisplayName == "Joe Bloggs", "The remote participant display name was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].RemoteParticipant.TargetURI == SIPURI.ParseSIPURI("sip:user@10.1.1.7:5070"), "The remote participant target URI was incorrect.");
                Assert.IsTrue(dialogInfo.DialogItems[0].RemoteParticipant.CSeq == 1, "The remote participant CSeq was incorrect.");

                Console.WriteLine(dialogInfo.ToXMLText());

                Console.WriteLine("-----------------------------------------");
            }

            /// <summary>
            /// Tests that the data in the SDP nodes is correctly parsed.
            /// </summary>
            /*[Test]
            public void ParseSDPFromXMLStringDialogUnitTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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
                Assert.IsTrue(dialogInfo.DialogItems[0].LocalParticipant.SDP == sdp, "The local participant SDP was parsed incorrectly.");
                Assert.IsTrue(dialogInfo.DialogItems[0].RemoteParticipant.SDP == sdp, "The remote participant SDP was parsed incorrectly.");

                Console.WriteLine(dialogInfo.ToXMLText());

                Console.WriteLine("-----------------------------------------");
            }*/
        }

        #endif

        #endregion
    }
}
