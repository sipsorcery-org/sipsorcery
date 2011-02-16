// ============================================================================
// FileName: ThirtySevenSignalsCompanyUnitTest.cs
//
// Description:
// Unit tests for the Company class from the 37 Signals contact management system Highrise.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Feb 2011  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.CRM.ThirtySevenSignals;

namespace SIPSorcery.CRM.UnitTests
{
    /// <summary>
    /// Tests for the 37Signals company class.
    /// </summary>
    [TestClass]
    public class ThirtySevenSignalsCompanyUnitTest
    {
        public ThirtySevenSignalsCompanyUnitTest()
        { }

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

        [TestMethod]
        public void DeserialisationTest()
        {
            string companyXML =
                "<company>" +
                "  <id type=\"integer\">1</id>" +
                "  <name>Doe Inc.</name>" +
                "  <background>A popular company for random data</background>" +
                "  <created-at type=\"datetime\">2007-02-27T03:11:52Z</created-at>" +
                "  <updated-at type=\"datetime\">2007-03-10T15:11:52Z</updated-at>" +
                "  <visible-to>Everyone</visible-to>" +
                "  <owner-id type=\"integer\" nil=\"true\"></owner-id>" +
                "  <group-id type=\"integer\" nil=\"true\"></group-id>" +
                "  <author-id type=\"integer\">2</author-id>" +
                "  <contact-data>" +
                "    <email-addresses>" +
                "      <email-address>" +
                "        <id type=\"integer\">1</id>" +
                "        <address>corporate@example.com</address>" +
                "        <location>Work</location>" +
                "      </email-address>" +
                "    </email-addresses>" +
                "    <phone-numbers>" +
                "      <phone-number>" +
                "        <id type=\"integer\">2</id>" +
                "        <number>555-555-5555</number>" +
                "        <location>Work</location>" +
                "      </phone-number>" +
                "      <phone-number>" +
                "        <id type=\"integer\">3</id>" +
                "        <number>555-666-6667</number>" +
                "        <location>Fax</location>" +
                "      </phone-number>" +
                "    </phone-numbers>" +
                "  </contact-data>" +
                "  <tags type=\"array\">" +
                "    <tag>" +
                "      <id type=\"integer\">2</id>" +
                "      <name>Lead</name>" +
                "    </tag>" +
                "  </tags>" +
                "</company>";

            companyXML = Regex.Replace(companyXML, "nil=\"true\"", "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"");

            Company company = null;

            using (TextReader reader = new StringReader(companyXML))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Company));
                company = (Company)serializer.Deserialize(reader);
            }

            Assert.IsNotNull(company, "The company object was not deserialised from the XML.");
            Assert.AreEqual(company.ID, 1, "The company's id was not deserialised correctly.");
            Assert.AreEqual(company.Background, "A popular company for random data", "The company's background was not deserialised correctly.");
            Assert.AreEqual(company.CreatedAtStr, "2007-02-27T03:11:52Z", "The company's created at date string was not deserialised correctly.");
            Assert.AreEqual(company.CreatedAt.ToUniversalTime().ToString(Company.DATETIME_STRING_FORMAT), "2007-02-27T03:11:52Z", "The company's created at datetime offset was not deserialised correctly.");
            Assert.AreEqual(company.UpdatedAtStr, "2007-03-10T15:11:52Z", "The company's updated at date string was not deserialised correctly.");
            Assert.AreEqual(company.UpdatedAt.ToUniversalTime().ToString(Company.DATETIME_STRING_FORMAT), "2007-03-10T15:11:52Z", "The company's updated at datetime offset was not deserialised correctly.");
            Assert.AreEqual(company.VisibleTo, "Everyone", "The company's visible to value was not deserialised correctly.");
            Assert.IsNull(company.OwnerID, "The company's owner id was not deserialised correctly.");
            Assert.IsNull(company.GroupID, "The company's group id was not deserialised correctly.");
            Assert.AreEqual(company.AuthorID, 2, "The company's author id was not deserialised correctly.");
            Assert.IsNotNull(company.ContactData, "The company's contact data was not deserialised correctly.");
            Assert.AreEqual(company.ContactData.PhoneNumbers.Count, 2, "The company's phone numbers did not deserialise to the correct count.");
            Assert.AreEqual(company.ContactData.PhoneNumbers[0].ID, 2, "The company's phone number id did not deserialise correctly.");
            Assert.AreEqual(company.ContactData.PhoneNumbers[0].Location, "Work", "The company's phone number location did not deserialise correctly.");
            Assert.AreEqual(company.ContactData.PhoneNumbers[0].Number, "555-555-5555", "The company's phone number number did not deserialise correctly.");
        }
    }
}
