using System;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.SIP.Core.UnitTests
{
    [TestClass]
    public class SIPEventPresenceUnitTest
    {
        /// <summary>
        /// Used to check the conformance of blocks of XML text to the schema in RFC3863.
        ///// </summary>
        //[TestMethod]
        ////[Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
        //[ExpectedException(typeof(XmlSchemaValidationException))]
        //public void InvalidXMLUnitTest()
        //{
        //    Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    if (m_presenceSchema == null)
        //    {
        //        Console.WriteLine("Loading XSD schema for dialog event package, takes a while...");

        //        m_presenceSchema = new XmlSchemaSet();
        //        XmlReader schemaReader = new XmlTextReader(SIPSorcery.SIP.Properties.Resources.PIDFSchema, XmlNodeType.Document, null);
        //        m_presenceSchema.Add(m_pidfXMLNS, schemaReader);
        //    }

        //    // The mandatory entity attribue on the presence element is missing.
        //    string invalidPresenceXMLStr =
        //         "<?xml version='1.0' encoding='utf-16'?>" +
        //         "<presence xmlns='urn:ietf:params:xml:ns:pidf'>" +
        //         "</presence>";

        //    XDocument presenceDoc = XDocument.Parse(invalidPresenceXMLStr);
        //    presenceDoc.Validate(m_presenceSchema, (o, e) =>
        //    {
        //        Console.WriteLine("XSD validation " + e.Severity + " event: " + e.Message);

        //        if (e.Severity == XmlSeverityType.Error)
        //        {
        //            throw e.Exception;
        //        }
        //    });

        //    Console.WriteLine("-----------------------------------------");
        //}

        ///// <summary>
        ///// Used to check the conformance of blocks of XML text to the schema in RFC 4235.
        ///// </summary>
        //[TestMethod]
        ////[Ignore("Use this method to validate dialog XML packages against the RFC schema. It takes a little bit of time to load the schema.")]
        //public void ValidXMLUnitTest()
        //{
        //    Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    if (m_presenceSchema == null)
        //    {
        //        Console.WriteLine("Loading XSD schema for dialog event package, takes a while...");

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
        //        Console.WriteLine("XSD validation " + e.Severity + " event: " + e.Message);

        //        if (e.Severity == XmlSeverityType.Error)
        //        {
        //            throw e.Exception;
        //        }
        //    });

        //    Console.WriteLine("-----------------------------------------");
        //}

        ///// <summary>
        ///// Tests that a SIPEventPresence object will generate an XML text representation of itself without throwing any exceptions.
        ///// </summary>
        //[TestMethod]
        //public void GetAsXMLStringUnitTest()
        //{
        //    Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

        //    SIPEventPresence presence = new SIPEventPresence(SIPURI.ParseSIPURI("sip:me@somewhere.com"));
        //    presence.Tuples.Add(new SIPEventPresenceTuple("1234", SIPEventPresenceStateEnum.open, SIPURI.ParseSIPURIRelaxed("test@test.com"), 0.8M));

        //    Console.WriteLine(presence.ToXMLText());

        //    Console.WriteLine("-----------------------------------------");
        //}

        ///// <summary>
        ///// Tests that a single tuple block of XML text is correctly parsed and the value of each individual item is correctly extracted.
        ///// </summary>
        //[TestMethod]
        //public void ParseFromXMLStringUnitTest()
        //{
        //    Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

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

        //    Assert.IsTrue(presence.Entity.ToString() == "sip:test@test.com", "The parsed presence event entity was incorrect.");
        //    Assert.IsTrue(presence.Tuples.Count == 1, "The parsed presence event tuple number was incorrect.");
        //    Assert.IsTrue(presence.Tuples[0].ID == "as7d900as8", "The parsed presence event first tuple ID was incorrect.");
        //    Assert.IsTrue(presence.Tuples[0].Status == SIPEventPresenceStateEnum.open, "The parsed presence event first tuple status was incorrect.");
        //    Assert.IsTrue(presence.Tuples[0].ContactURI.ToString() == "sip:test123@test.com", "The parsed presence event first tuple Contact URI was incorrect.");
        //    Assert.IsTrue(presence.Tuples[0].ContactPriority == 1.2M, "The parsed presence event first tuple Contact priority was incorrect.");

        //    Console.WriteLine("-----------------------------------------");
        //}
    }
}
