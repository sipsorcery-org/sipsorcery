// ============================================================================
// FileName: ThirtySevenSignalsPersonUnitTest.cs
//
// Description:
// Unit tests for the Person class from the 37 Signals contact management system Highrise.
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
    /// Tests for the 37Signals person class.
    /// </summary>
    [TestClass]
    public class ThirtySevenSignalsPersonUnitTest
    {
        public ThirtySevenSignalsPersonUnitTest()
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
            string personXML =
                "<person>" +
                "<author-id type=\"integer\">391244</author-id>" +
                "<background>Smart cookie</background>" +
                "<company-id type=\"integer\">57644826</company-id> " +
                "<created-at type=\"datetime\">2011-01-12T10:17:53Z</created-at> " +
                "<first-name>Joe</first-name>" +
                "<group-id type=\"integer\" nil=\"true\" />" +
                "  <id type=\"integer\">57644825</id> " +
                "  <last-name>Bloggs</last-name> " +
                "  <owner-id type=\"integer\" nil=\"true\" />" +
                "  <title>Mr</title> " +
                "  <updated-at type=\"datetime\">2011-01-29T01:10:39Z</updated-at> " +
                "  <visible-to>Everyone</visible-to> " +
                " <contact-data>" +
                " <phone-numbers type=\"array\">" +
                " <phone-number>" +
                "  <id type=\"integer\">51564454</id> " +
                "  <location>Work</location> " +
                "  <number>12345678</number> " +
                "  </phone-number>" +
                "  </phone-numbers>" +
                "  <addresses type=\"array\" /> " +
                "  <web-addresses type=\"array\" />" +
                "  <email-addresses type=\"array\" /> " +
                "  <instant-messengers type=\"array\" /> " +
                "  <twitter-accounts type=\"array\" />" +
                "  </contact-data>" +
                "  </person>";

            personXML = Regex.Replace(personXML, "nil=\"true\"", "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"");

            Person person = null;

            using (TextReader reader = new StringReader(personXML))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Person));
                person = (Person)serializer.Deserialize(reader);
            }

            Assert.IsNotNull(person, "The person object was not deserialised from the XML.");
            Assert.AreEqual(person.ID, 57644825, "The person's id was not deserialised correctly.");
            Assert.AreEqual(person.AuthorID, 391244, "The person's author id was not deserialised correctly.");
            Assert.AreEqual(person.FirstName, "Joe", "The person's first name was not deserialised correctly.");
            Assert.AreEqual(person.LastName, "Bloggs", "The person's last name was not deserialised correctly.");
            Assert.AreEqual(person.Title, "Mr", "The person's title was not deserialised correctly.");
            Assert.AreEqual(person.Background, "Smart cookie", "The person's background was not deserialised correctly.");
            Assert.AreEqual(person.CompanyID, 57644826, "The person's company id was not deserialised correctly.");
            Assert.AreEqual(person.CreatedAtStr, "2011-01-12T10:17:53Z", "The person's created at date string was not deserialised correctly.");
            Assert.AreEqual(person.CreatedAt.ToUniversalTime().ToString(Person.DATETIME_STRING_FORMAT), "2011-01-12T10:17:53Z", "The person's created at datetime offset was not deserialised correctly.");
            Assert.AreEqual(person.UpdatedAtStr, "2011-01-29T01:10:39Z", "The person's updated at date string was not deserialised correctly.");
            Assert.AreEqual(person.UpdatedAt.ToUniversalTime().ToString(Person.DATETIME_STRING_FORMAT), "2011-01-29T01:10:39Z", "The person's updated at datetime offset was not deserialised correctly.");
            Assert.AreEqual(person.VisibleTo, "Everyone", "The person's visible to value was not deserialised correctly.");
            Assert.IsNull(person.OwnerID, "The person's owner id was not deserialised correctly.");
            Assert.IsNull(person.GroupID, "The person's group id was not deserialised correctly.");
            Assert.IsNotNull(person.ContactData, "The person's contact data was not deserialised correctly.");
            Assert.AreEqual(person.ContactData.PhoneNumbers.Count, 1, "The person's phone numbers did not deserialise to the correct count.");
            Assert.AreEqual(person.ContactData.PhoneNumbers[0].ID, 51564454, "The person's phone number id did not deserialise correctly.");
            Assert.AreEqual(person.ContactData.PhoneNumbers[0].Location, "Work", "The person's phone number location did not deserialise correctly.");
            Assert.AreEqual(person.ContactData.PhoneNumbers[0].Number, "12345678", "The person's phone number number did not deserialise correctly.");
        }

        [TestMethod]
        public void DeserialisationNullCompanyIDTest()
        {
            string personXML =
                "<person>" +
                "<author-id type=\"integer\">391244</author-id>" +
                "<background>Smart cookie</background>" +
                "<company-id type=\"integer\" nil=\"true\"></company-id> " +
                "<created-at type=\"datetime\">2011-01-12T10:17:53Z</created-at> " +
                "<first-name>Joe</first-name>" +
                "<group-id type=\"integer\" nil=\"true\" />" +
                "  <id type=\"integer\">57644825</id> " +
                "  <last-name>Bloggs</last-name> " +
                "  <owner-id type=\"integer\" nil=\"true\" />" +
                "  <title>Mr</title> " +
                "  <updated-at type=\"datetime\">2011-01-29T01:10:39Z</updated-at> " +
                "  <visible-to>Everyone</visible-to> " +
                " <contact-data>" +
                " <phone-numbers type=\"array\">" +
                " <phone-number>" +
                "  <id type=\"integer\">51564454</id> " +
                "  <location>Work</location> " +
                "  <number>12345678</number> " +
                "  </phone-number>" +
                "  </phone-numbers>" +
                "  <addresses type=\"array\" /> " +
                "  <web-addresses type=\"array\" />" +
                "  <email-addresses type=\"array\" /> " +
                "  <instant-messengers type=\"array\" /> " +
                "  <twitter-accounts type=\"array\" />" +
                "  </contact-data>" +
                "  </person>";

            personXML = Regex.Replace(personXML, "nil=\"true\"", "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"");

            Person person = null;

            using (TextReader reader = new StringReader(personXML))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Person));
                person = (Person)serializer.Deserialize(reader);
            }

            Assert.IsNull(person.CompanyID, "The person's company id was not deserialised correctly.");
        }

        /// <summary>
        /// Test deserialising an XML array of person data.
        /// </summary>
        [TestMethod]
        public void DeserialisationPeopleTest()
        {
            string peopleXML =
                "<people>" +
                "<person>" +
                "<author-id type=\"integer\">391244</author-id>" +
                "<background>Smart cookie</background>" +
                "<company-id type=\"integer\" nil=\"true\"></company-id> " +
                "<created-at type=\"datetime\">2011-01-12T10:17:53Z</created-at> " +
                "<first-name>Joe</first-name>" +
                "<group-id type=\"integer\" nil=\"true\" />" +
                "  <id type=\"integer\">57644825</id> " +
                "  <last-name>Bloggs</last-name> " +
                "  <owner-id type=\"integer\" nil=\"true\" />" +
                "  <title>Mr</title> " +
                "  <updated-at type=\"datetime\">2011-01-29T01:10:39Z</updated-at> " +
                "  <visible-to>Everyone</visible-to> " +
                " <contact-data>" +
                " <phone-numbers type=\"array\">" +
                " <phone-number>" +
                "  <id type=\"integer\">51564454</id> " +
                "  <location>Work</location> " +
                "  <number>12345678</number> " +
                "  </phone-number>" +
                "  </phone-numbers>" +
                "  <addresses type=\"array\" /> " +
                "  <web-addresses type=\"array\" />" +
                "  <email-addresses type=\"array\" /> " +
                "  <instant-messengers type=\"array\" /> " +
                "  <twitter-accounts type=\"array\" />" +
                "  </contact-data>" +
                "  </person>" +
                "</people>";

            peopleXML = Regex.Replace(peopleXML, "nil=\"true\"", "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"");

            People people = null;

            using (TextReader reader = new StringReader(peopleXML))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(People));
                people = (People)serializer.Deserialize(reader);
            }

            Assert.AreEqual(1, people.PersonList.Count, "The number of people deserialised was incorrect.");
            Assert.AreEqual(57644825, people.PersonList[0].ID, "The person's deserialised id was incorrect.");
        }

        [TestMethod]
        public void SetAvatarURLTest()
        {
            string url = "https://somebiz.highrisehq.com";
            Person person = new Person() { ID = 12345678 };
            person.SetAvatarURL(url);

            Assert.AreEqual(String.Format(Person.AVATAR_URL_FORMAT, url, 1234, 5678), person.AvatarURL, "The person's avatar URL was not set correctly.");
        }
    }
}
