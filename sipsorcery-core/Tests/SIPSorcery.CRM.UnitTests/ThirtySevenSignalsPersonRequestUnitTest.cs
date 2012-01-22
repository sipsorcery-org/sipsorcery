// ============================================================================
// FileName: ThirtySevenSignalsPersonRequestUnitTest.cs
//
// Description:
// Unit tests for the class that requests Person objects from the 37 Signals contact management system Highrise.
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
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.CRM.ThirtySevenSignals;

namespace SIPSorcery.CRM.UnitTests
{
    [TestClass]
    public class ThirtySevenSignalsPersonRequestUnitTest
    {
        private string m_highriseURL = "https://x.highrisehq.com";
        private string m_highriseAuthToken = "";

        [TestMethod]
        public void PersonRequestByIDTest()
        {
            int id = 83447815;
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            Person person = request.GetByID(id);

            Console.WriteLine(person.AvatarURL);

            Assert.AreEqual(id, person.ID, "The ID of the person record did not match the ID that was requested.");
        }

        [TestMethod]
        public void GetPeopleByPhoneNumberTest()
        {
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            People people = request.GetByPhoneNumber("555");

            Assert.IsNotNull(people, "The people should not have been null.");
        }

        [TestMethod]
        public void GetPeopleByNameTest()
        {
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            People people = request.GetByName("Agent Smith");

            Assert.IsNotNull(people, "The people should not have been null.");
            Assert.AreEqual(1, people.PersonList.Count, "An unexpected number of results were returned.");
            Assert.AreEqual(people.PersonList[0].ID, 59708303, "The ID of the person returned for a name search was not expected.");
        }

        [TestMethod]
        public void GetPeopleByNameDoesntExistTest()
        {
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            People people = request.GetByName("I Dont Exist");

            Assert.IsNull(people);
        }

        [TestMethod]
        public void GetPeopleByCustomFieldExistTest()
        {
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            People people = request.GetByCustomField("sip_address", "aaronip500@sipsorcery.com");

            Assert.IsNotNull(people, "The people should not have been null.");
            Assert.AreEqual(1, people.PersonList.Count, "An unexpected number of results were returned.");
            Assert.AreEqual(people.PersonList[0].ID, 59708319, "The ID of the person returned for a name search was not expected.");
        }

        [TestMethod]
        public void GetPeopleByCustomFieldPrefixSearchExistTest()
        {
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            People people = request.GetByCustomField("sip_address", "aaronip500");

            Assert.IsNotNull(people, "The people should not have been null.");
            Assert.AreEqual(1, people.PersonList.Count, "An unexpected number of results were returned.");
            Assert.AreEqual(people.PersonList[0].ID, 59708319, "The ID of the person returned for a name search was not expected.");
        }

        [TestMethod]
        [Ignore]   // Did not manage to get a multi-criteria search to return any results.
        public void GetPeopleByCustomSearchCriteriaTest()
        {
            PersonRequest request = new PersonRequest(m_highriseURL, m_highriseAuthToken);
            People people = request.GetByCustomSearch("criteria[sip_address]=aaronip500@sipsorcery.com&criteria[phone]=5556");

            Assert.IsNotNull(people, "The people should not have been null.");
            Assert.AreEqual(1, people.PersonList.Count, "An unexpected number of results were returned.");
            Assert.AreEqual(people.PersonList[0].ID, 59708319, "The ID of the person returned for a name search was not expected.");
        }
    }
}
