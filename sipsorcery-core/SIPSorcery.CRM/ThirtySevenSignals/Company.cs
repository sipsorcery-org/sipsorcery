// ============================================================================
// FileName: Company.cs
//
// Description:
// Represents a Company object for the 37 Signals contact management system Highrise.
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
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace SIPSorcery.CRM.ThirtySevenSignals
{
    [Serializable()]
    [XmlRootAttribute("company", Namespace = "", IsNullable = false)]
    public class Company
    {
        public const string DATETIME_STRING_FORMAT = "yyyy-MM-ddTHH:mm:ssZ";

        [XmlElement("id")]
        public int ID { get; set; }

        [XmlElement("author-id")]
        public int AuthorID { get; set; }

        [XmlElement("name")]
        public string Name { get; set; }

        [XmlElement("background")]
        public string Background { get; set; }

        [XmlElement("created-at")]
        public string CreatedAtStr { get; set; }

        [XmlIgnore]
        public DateTimeOffset CreatedAt
        {
            get { return DateTimeOffset.Parse(CreatedAtStr); }
            set { CreatedAtStr = value.ToUniversalTime().ToString(DATETIME_STRING_FORMAT); }
        }

        [XmlElement("updated-at")]
        public string UpdatedAtStr { get; set; }

        [XmlIgnore]
        public DateTimeOffset UpdatedAt
        {
            get { return DateTimeOffset.Parse(UpdatedAtStr); }
            set { UpdatedAtStr = value.ToUniversalTime().ToString(DATETIME_STRING_FORMAT); }
        }

        [XmlElement("visible-to")]
        public string VisibleTo { get; set; }

        [XmlElement("owner-id", IsNullable=true)]
        public int? OwnerID { get; set; }

        [XmlElement("group-id", IsNullable = true)]
        public int? GroupID { get; set; }

        [XmlElement("contact-data")]
        public ContactData ContactData { get; set; }
    }
}
