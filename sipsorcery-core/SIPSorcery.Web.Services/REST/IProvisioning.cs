//-----------------------------------------------------------------------------
// Filename: IProvisioning.cs
//
// Description: The interface for the REST/JSON service to manipulate the resource
// entities exposed by sipsorcery.
// 
// History:
// 25 Oct 2011	Aaron Clauson	    Created.
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
    public class JSONSIPAccount
    {
        public string ID;
        public string Username;
    }

    [ServiceContract(Namespace = "http://www.sipsorcery.com/rest/v0.1")]
    public interface IProvisioning 
    {
        [OperationContract]
        [WebGet(UriTemplate = "isalive", ResponseFormat = WebMessageFormat.Json)]
        bool IsAlive();

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "customer/add", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> AddCustomer(CustomerJSON customer);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "customer/setservicelevel", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json, BodyStyle=WebMessageBodyStyle.WrappedRequest)]
        JSONResult<string> SetCustomerServiceLevel(string username, string serviceLevel, string renewalDate);

        [OperationContract]
        [WebGet(UriTemplate = "customer/setreadonly?username={username}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> SetReadOnly(string username);

        [OperationContract]
        [WebGet(UriTemplate = "sipdomain/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPDomain> GetSIPDomains(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccount/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<int> GetSIPAccountsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccount/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<List<SIPAccountJSON>> GetSIPAccounts(string where, int offset, int count);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "sipaccount/add", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> AddSIPAccount(SIPAccountJSON sipAccount);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "sipaccount/update", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> UpdateSIPAccount(SIPAccountJSON sipAccount);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccount/delete?id={id}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<bool> DeleteSIPAccount(string id);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccountbinding/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<int> GetSIPAccountBindingsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipaccountbinding/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<List<SIPRegistrarBindingJSON>> GetSIPAccountBindings(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "sipprovider/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<int> GetSIPProvidersCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipprovider/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<List<SIPProviderJSON>> GetSIPProviders(string where, int offset, int count);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "sipprovider/add", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> AddSIPProvider(SIPProviderJSON sipProvider);

        [OperationContract]
        [WebInvoke(Method = "POST", UriTemplate = "sipprovider/update", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
        JSONResult<string> UpdateSIPProvider(SIPProviderJSON sipAccount);

        [OperationContract]
        [WebGet(UriTemplate = "sipprovider/delete?id={id}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<bool> DeleteSIPProvider(string id);
        
        [OperationContract]
        [WebGet(UriTemplate = "sipproviderbinding/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<int> GetSIPProviderBindingsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "sipproviderbinding/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<List<SIPProviderBindingJSON>> GetSIPProviderBindings(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "dialplan/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetDialPlansCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "dialplan/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPDialPlan> GetDialPlans(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "call/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        int GetCallsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "call/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        List<SIPDialogue> GetCalls(string where, int offset, int count);

        [OperationContract]
        [WebGet(UriTemplate = "cdr/count?where={where}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<int> GetCDRsCount(string where);

        [OperationContract]
        [WebGet(UriTemplate = "cdr/get?where={where}&offset={offset}&count={count}", ResponseFormat = WebMessageFormat.Json)]
        JSONResult<List<CDRJSON>> GetCDRs(string where, int offset, int count);
    }
}
