//-----------------------------------------------------------------------------
// Filename: IAccounting.cs
//
// Description: The interface for a REST/JSON service that exposes customer accounting services.
// 
// History:
// 25 Sep 2012	Aaron Clauson	    Created.
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
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using SIPSorcery.Entities;

namespace SIPSorcery.Web.Services 
{
    [ServiceContract(Namespace = "http://www.sipsorcery.com/rest/accounting/v0.1")]
    public interface IAccounting 
    {
        [OperationContract]
        [WebGet(UriTemplate = "isalive", ResponseFormat = WebMessageFormat.Json)]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "customeraccount/verify?accountnumber={accountNumber}&pin={pin}", ResponseFormat = WebMessageFormat.Json)]
        bool VerifyCustomerAccount(string accountNumber, int pin);

        [OperationContract]
        [WebGet(UriTemplate = "customeraccount/exists?accountnumber={accountNumber}", ResponseFormat = WebMessageFormat.Json)]
        bool DoesAccountNumberExist(string accountNumber);
    }
}
