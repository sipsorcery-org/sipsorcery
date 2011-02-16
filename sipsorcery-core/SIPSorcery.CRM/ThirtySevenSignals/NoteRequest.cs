// ============================================================================
// FileName: NoteRequest.cs
//
// Description:
// Retrieves Note objects from the 37 Signals contact management system Highrise.
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
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Sys;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    public class NoteRequest : HighriseRequest<Note, Notes>
    {
        private const string URL_NOUN = "notes";

        public NoteRequest(string url, string authToken) :
            base(url, URL_NOUN, authToken)
        { }

        public string CreateNoteForPerson(int personID, string body)
        {
            string createURL = BaseUrl + "/people/" + personID + "/notes.xml";
            //string createURL = BaseUrl + "/notes.xml";

            string createXML = 
                "<note>" +
                " <body>" + SecurityElement.Escape(body) + "</body>" +
                //" <subject-id type=\"integer\">" + personID + "</subject-id>" +
                //" <subject-type>" + SubjectTypesEnum.Party + "</subject-type>" +
                "</note>";

            return base.CreateItem(createURL, createXML);
        }

        public string CreateNoteForCompany(int companyID, string body)
        {
            string createURL = BaseUrl + "/companies/" + companyID + "/notes.xml";

            string createXML =
                "<note>" +
                " <body>" + SecurityElement.Escape(body) + "</body>" +
                "</note>";

            return base.CreateItem(createURL, createXML);
        }
    }
}
