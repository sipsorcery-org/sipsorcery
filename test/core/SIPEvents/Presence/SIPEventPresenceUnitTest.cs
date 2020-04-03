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

using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPEventPresenceUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPEventPresenceUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Used to check the conformance of blocks of XML text to the schema in RFC3863.
        ///// </summary>
        //[Fact]
        ////[Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
        //[ExpectedException(typeof(XmlSchemaValidationException))]
        //public void InvalidXMLUnitTest()
        //{
        //    logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    if (m_presenceSchema == null)
        //    {
        //        logger.LogDebug("Loading XSD schema for dialog event package, takes a while...");

        //        m_presenceSchema = new XmlSchemaSet();
        //        XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.PIDFSchema, XmlNodeType.Document, null);
        //        m_presenceSchema.Add(m_pidfXMLNS, schemaReader);
        //    }

        //    // The mandatory entity attribute on the presence element is missing.
        //    string invalidPresenceXMLStr =
        //         "<?xml version='1.0' encoding='utf-16'?>" +
        //         "<presence xmlns='urn:ietf:params:xml:ns:pidf'>" +
        //         "</presence>";

        //    XDocument presenceDoc = XDocument.Parse(invalidPresenceXMLStr);
        //    presenceDoc.Validate(m_presenceSchema, (o, e) =>
        //    {
        //        logger.LogDebug("XSD validation " + e.Severity + " event: " + e.Message);

        //        if (e.Severity == XmlSeverityType.Error)
        //        {
        //            throw e.Exception;
        //        }
        //    });

        //    logger.LogDebug("-----------------------------------------");
        //}

        ///// <summary>
        ///// Used to check the conformance of blocks of XML text to the schema in RFC 4235.
        ///// </summary>
        //[Fact]
        ////[Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
        //public void ValidXMLUnitTest()
        //{
        //    logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    if (m_presenceSchema == null)
        //    {
        //        logger.LogDebug("Loading XSD schema for dialog event package, takes a while...");

        //        m_presenceSchema = new XmlSchemaSet();
        //        XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.PIDFSchema, XmlNodeType.Document, null);
        //        m_presenceSchema.Add(m_pidfXMLNS, schemaReader);
        //    }

        //    string validPresenceXMLStr =
        //        "<?xml version='1.0' encoding='UTF-8'?>" +
        //        "<presence xmlns='urn:ietf:params:xml:ns:pidf' entity='pres:someone@example.com'>" +
        //        " <tuple id='sg89ae'>" +
        //        "  <status>" +
        //        "   <basic>open</basic>" +
        //        "  </status>" +
        //        "  <contact priority='0.8'>tel:+09012345678</contact>" +
        //        " </tuple>" +
        //        "</presence>";

        //    XDocument presenceDoc = XDocument.Parse(validPresenceXMLStr);
        //    presenceDoc.Validate(m_presenceSchema, (o, e) =>
        //    {
        //        logger.LogDebug("XSD validation " + e.Severity + " event: " + e.Message);

        //        if (e.Severity == XmlSeverityType.Error)
        //        {
        //            throw e.Exception;
        //        }
        //    });

        //    logger.LogDebug("-----------------------------------------");
        //}

        ///// <summary>
        ///// Tests that a SIPEventPresence object will generate an XML text representation of itself without throwing any exceptions.
        ///// </summary>
        //[Fact]
        //public void GetAsXMLStringUnitTest()
        //{
        //    logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    SIPEventPresence presence = new SIPEventPresence(SIPURI.ParseSIPURI("sip:me@somewhere.com"));
        //    presence.Tuples.Add(new SIPEventPresenceTuple("1234", SIPEventPresenceStateEnum.open, SIPURI.ParseSIPURIRelaxed("test@test.com"), 0.8M));

        //    logger.LogDebug(presence.ToXMLText());

        //    logger.LogDebug("-----------------------------------------");
        //}

        ///// <summary>
        ///// Tests that a single tuple block of XML text is correctly parsed and the value of each individual item is correctly extracted.
        ///// </summary>
        //[Fact]
        //public void ParseFromXMLStringUnitTest()
        //{
        //    logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    string presenceXMLStr = "<?xml version='1.0' encoding='utf-16'?>" +
        //         "<presence entity='sip:test@test.com' xmlns='urn:ietf:params:xml:ns:pidf'>" +
        //         " <tuple id='as7d900as8'>" +
        //         "  <status>" +
        //         "   <basic>open</basic>" +
        //         "  </status>" +
        //         "  <contact priority='1.2'>sip:test123@test.com</contact>" +
        //         " </tuple>" +
        //         "</presence>";

        //    SIPEventPresence presence = SIPEventPresence.Parse(presenceXMLStr);

        //    Assert.True(presence.Entity.ToString() == "sip:test@test.com", "The parsed presence event entity was incorrect.");
        //    Assert.True(presence.Tuples.Count == 1, "The parsed presence event tuple number was incorrect.");
        //    Assert.True(presence.Tuples[0].ID == "as7d900as8", "The parsed presence event first tuple ID was incorrect.");
        //    Assert.True(presence.Tuples[0].Status == SIPEventPresenceStateEnum.open, "The parsed presence event first tuple status was incorrect.");
        //    Assert.True(presence.Tuples[0].ContactURI.ToString() == "sip:test123@test.com", "The parsed presence event first tuple Contact URI was incorrect.");
        //    Assert.True(presence.Tuples[0].ContactPriority == 1.2M, "The parsed presence event first tuple Contact priority was incorrect.");

        //    logger.LogDebug("-----------------------------------------");
        //}
    }
}
