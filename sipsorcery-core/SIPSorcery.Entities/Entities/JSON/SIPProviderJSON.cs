//-----------------------------------------------------------------------------
// Filename: SIPProviderJSON.cs
//
// Description: A translation class to allow a SIP provider to be serialised to and from JSON. The 
// Entity Framework derived SIPProvider class cannot be used to a shortcoming in the serialisation
// mechanism to do with IsReference classes. This will probably be fixed in the future making this
// class redundant.
// 
// History:
// 27 Oct 2011	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Pty. Ltd. 
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Entities
{
    [DataContractAttribute]
    public class SIPProviderJSON
    {
        [DataMember] public string ID { get; set; }
        [DataMember] public string ProviderName { get; set; }
        [DataMember] public string ProviderUsername { get; set; }
        [DataMember] public string ProviderPassword { get; set; }
        [DataMember] public string ProviderServer { get; set; }
        [DataMember] public string ProviderAuthUsername { get; set; }
        [DataMember] public string ProviderOutboundProxy { get; set; }
        [DataMember] public string ProviderType { get; set; }
        [DataMember] public string ProviderFrom { get; set; }
        [DataMember] public string CustomHeaders { get; set; }
        [DataMember] public string RegisterContact { get; set; }
        [DataMember] public int RegisterExpiry { get; set; }
        [DataMember] public string RegisterServer { get; set; }
        [DataMember] public string RegisterRealm { get; set; }
        [DataMember] public bool RegisterEnabled { get; set; }
        [DataMember] public string GVCallbackNumber { get; set; }
        [DataMember] public string GVCallbackPattern { get; set; }
        [DataMember] public string GVCallbackType { get; set; }

        public SIPProviderJSON()
        { }

        public SIPProvider ToSIPProvider()
        {
            var entitySIPProvider = new SIPProvider()
            {
                Owner = String.Empty,
                ID = ID,   
                ProviderName = ProviderName,
                ProviderUsername = ProviderUsername,
                ProviderPassword = ProviderPassword,
                ProviderServer = ProviderServer,
                ProviderAuthUsername = ProviderAuthUsername,
                ProviderOutboundProxy = ProviderOutboundProxy,
                ProviderType = ProviderType,
                ProviderFrom = ProviderFrom,
                CustomHeaders = CustomHeaders,
                RegisterContact = RegisterContact,
                RegisterExpiry = RegisterExpiry,
                RegisterServer = RegisterServer,
                RegisterRealm = RegisterRealm,
                GVCallbackNumber = GVCallbackNumber,
                GVCallbackPattern = GVCallbackPattern,
                GVCallbackType = GVCallbackType
            };

            return entitySIPProvider;
        }
    }
}
