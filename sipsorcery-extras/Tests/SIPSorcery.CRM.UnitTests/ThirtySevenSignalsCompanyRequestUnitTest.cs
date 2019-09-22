// ============================================================================
// FileName: ThirtySevenSignalsCompanyRequestUnitTest.cs
//
// Description:
// Unit tests for the class that requests Company objects from the 37 Signals contact management system Highrise.
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
    [Ignore] // Need authentication token.
    public class ThirtySevenSignalsCompanyRequestUnitTest
    {
        private string m_highriseURL = "https://x.highrisehq.com";
        private string m_highriseAuthToken = "";

        [TestMethod]
        public void CompanyRequestByIDTest()
        {
            int id = 57644826;
            CompanyRequest request = new CompanyRequest(m_highriseURL, m_highriseAuthToken);
            Company company = request.GetByID(id);

            Assert.AreEqual(id, company.ID, "The ID of the company record did not match the ID that was requested.");
            Assert.AreEqual("Acme Washing Machines", company.Name, "The name of the company was not as expected.");
        }

        [TestMethod]
        public void GetCompaniesByPhoneNumberTest()
        {
            CompanyRequest request = new CompanyRequest(m_highriseURL, m_highriseAuthToken);
            Companies companies = request.GetByPhoneNumber("555");

            Assert.IsNotNull(companies, "The companies result should not have been null.");
        }

        [TestMethod]
        public void GetCompaniesByNameTest()
        {
            CompanyRequest request = new CompanyRequest(m_highriseURL, m_highriseAuthToken);
            Companies companies = request.GetByName("Acme Washing Machines");

            Assert.IsNotNull(companies, "The companies result should not have been null.");
            Assert.AreEqual(1, companies.CompanyList.Count, "An unexpected number of results were returned.");
            Assert.AreEqual(companies.CompanyList[0].ID, 57644826, "The ID of the company returned for a name search was not expected.");
        }
    }
}
