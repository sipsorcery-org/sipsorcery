// ============================================================================
// FileName: AssemblyState.cs
//
// Description:
//  Holds application configuration information.
//
// Author(s):
// Aaron Clauson
//
// History:
// 08 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
	public class AssemblyState
	{
        public const string XML_DOMAINS_FILENAME = "sipdomains.xml";
        public const string XML_SIPACCOUNTS_FILENAME = "sipaccounts.xml";
        public const string XML_SIPPROVIDERS_FILENAME = "sipproviders.xml";
        public const string XML_DIALPLANS_FILENAME = "sipdialplans.xml";
        public const string XML_REGISTRAR_BINDINGS_FILENAME = "sipregistrarbindings.xml";
        public const string XML_PROVIDER_BINDINGS_FILENAME = "sipproviderbindings.xml";
        public const string XML_SIPDIALOGUES_FILENAME = "sipdialogues.xml";
        public const string XML_SIPCDRS_FILENAME = "sipcdrs.xml";
	}
}