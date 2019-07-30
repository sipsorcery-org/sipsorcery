// ============================================================================
// FileName: SIPDialPlanScriptTypes.cs
//
// Description:
// The list of script types available for the dial plan contents.
//
// Author(s):
// Aaron Clauson
//
// History:
// 29 Sep 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Collections;
using System.Runtime.Serialization;

namespace SIPSorcery.SIP.App
{
    [DataContractAttribute]
    public enum SIPDialPlanScriptTypesEnum
    {
        Unknown = 0,
        Asterisk = 1,
        Ruby = 2,
        Python = 3,
        JScript = 4,
        Wizard = 5,
        TelisWizard = 6,
        SimpleWizard = 7,
    }

    public class SIPDialPlanScriptTypes
    {
        public static SIPDialPlanScriptTypesEnum GetSIPDialPlanScriptType(string scriptType)
        {
            return (SIPDialPlanScriptTypesEnum)Enum.Parse(typeof(SIPDialPlanScriptTypesEnum), scriptType, true);
        }
    }
}
