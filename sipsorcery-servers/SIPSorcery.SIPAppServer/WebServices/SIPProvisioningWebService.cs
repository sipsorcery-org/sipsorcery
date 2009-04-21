//-----------------------------------------------------------------------------
// Filename: SIPProvisioningWebService.cs
//
// Description: Web services that expose provisioning services for SIP assets such
// as SIPAccounts, SIPProivders etc. This web service deals with storing objects that need
// to be presisted as oppossed to the manager web service which deals with transient objects
// such as SIP acocunt bindings or registrations.
// 
// History:
// 25 Sep 2008	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
using System.Text;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Threading;
using System.Xml;
using BlueFace.Sys;
using BlueFace.VoIP.App.SIP;
using BlueFace.VoIP.SIP.StatefulProxy;
using log4net;

namespace SIPSorcery.WebServices
{
    [ServiceContract(Namespace = "http://www.sipsorcery.com")]
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    public class SIPProvisioningWebService
    {
        private ILog logger = log4net.LogManager.GetLogger("provisioning");

        public SIPSwitchPersistor Persistor;
        public AuthenticateWebServiceDelegate AuthenticateWebService_External;
        public AuthenticateTokenDelegate AuthenticateToken_External;
        public ExpireTokenDelegate ExpireToken_External;

        public SIPProvisioningWebService()
        {}

        [OperationContract]
        public bool IsAlive()
        {
            string authId = OperationContext.Current.IncomingMessageHeaders.GetHeader<string>("authid", "");
            logger.Debug("SIPProvisioningWebService IsAlive (authid=" + authId + ")");

            return true;
        }

        [OperationContract]
        public string Login(string username, string password)
        {
            logger.Debug("SIPProvisioningWebService Login called for " + username + ".");

            if (username == null || username.Trim().Length == 0)
            {
                return null;
            }
            else
            {
                Guid authId = AuthenticateWebService_External(username, password);
                if (authId != Guid.Empty)
                {
                    return authId.ToString();
                }
                else
                {
                    return null;
                }
            }
        }

        [OperationContract]
        public void Logout(string authId)
        {
            logger.Debug("SIPProvisioningWebService Logout called for " + authId + ".");

            ExpireToken_External(authId);
        }

        [OperationContract]
        public SIPAccount AddSIPAccount(SIPAccount sipAccount)
        {
            logger.Debug("SIPProvisioningService AddSIPAccount for " + sipAccount.Owner + " and " + sipAccount.SIPUsername + ".");

            return Persistor.AddSIPAccount(sipAccount);
        }

        [OperationContract]
        public int GetSIPAccountsCount(string whereExpression)
        {
            logger.Debug("SIPProvisioningService GetSIPAccounts.");

            return Persistor.GetSIPAccountsCount(whereExpression);
        }

        [OperationContract]
        public List<SIPAccount> GetSIPAccounts(string whereExpression, int offset, int count)
        {
            logger.Debug("SIPProvisioningService GetSIPAccounts.");

            return Persistor.GetSIPAccounts(whereExpression, offset, count);
        }

        [OperationContract]
        public SIPAccount UpdateSIPAccount(SIPAccount sipAccount)
        {
            logger.Debug("SIPProvisioningService UpdateSIPAccount for " + sipAccount.SIPUsername + ".");

            return Persistor.UpdateSIPAccount(sipAccount);
        }

        [OperationContract]
        public SIPAccount DeleteSIPAccount(SIPAccount sipAccount)
        {
            logger.Debug("SIPProvisioningService DeleteSIPAccount for " + sipAccount.SIPUsername + ".");

            Persistor.DeleteSIPAccount(sipAccount);

            // Enables the caller to see which SIP account has been deleted.
            return sipAccount;
        }

        [OperationContract]
        public List<DialPlan> GetDialPlans(string whereExpression)
        {
            logger.Debug("SIPProvisioningService GetDialPlans.");

            return Persistor.GetDialPlans(whereExpression);
        }

        [OperationContract]
        public List<SIPExtension> GetExtensions(string whereExpression)
        {
            logger.Debug("SIPProvisioningService GetExtensions.");

            return Persistor.GetExtensions(whereExpression);
        }

        [OperationContract]
        public SIPProvider AddSIPProvider(SIPProvider sipProvider)
        {
            logger.Debug("SIPProvisioningService AddSIPProvider for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

            return Persistor.AddSIPProvider(sipProvider);
        }

        [OperationContract]
        public List<SIPProvider> GetSIPProviders(string whereExpression)
        {
            logger.Debug("SIPProvisioningService GetSIPProviders.");

            return Persistor.GetSIPProvidersForUser(whereExpression);
        }

        [OperationContract]
        public SIPProvider UpdateSIPProvider(SIPProvider sipProvider)
        {
            logger.Debug("SIPProvisioningService UpdateSIPProvider for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

            return Persistor.UpdateSIPProvider(sipProvider);
        }

        [OperationContract]
        public void DeleteSIPProvider(SIPProvider sipProvider)
        {
            logger.Debug("SIPProvisioningService DeleteSIPProvider for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

            Persistor.DeleteSIPProvider(sipProvider);
        }

        [OperationContract]
        public List<string> GetDomains(string owner)
        {
            logger.Debug("SIPProvisioningService GetDomains for " + owner + ".");

            return Persistor.GetDomains(owner);
        }

        /// <summary>
        /// Informs the hosting agent that either a dialplan or a SIPProvider belonging to the dialplan has been updated and that the dialplan
        /// should be reloaded when next required. This method is redundant when an XML persistence layer is in use.
        /// </summary>
        /// <param name="owner">The dialplan owner.</param>
        /// <param name="dialplanName">The name allocated to the dialplan by the owner.</param>
        [OperationContract]
        public void DialPlanUpdated(string owner, string dialplanName)
        {
            Persistor.DialPlanExternalUpdate(owner, dialplanName);
        }

        /// <summary>
        /// Informs that hosting agent that a SIPProvider has been deleted. This method is redundant when an XML persistence layer is in use.
        /// </summary>
        /// <param name="sipProviderId">The id of the deleted provider.</param>
        [OperationContract]
        public void SIPProviderDeleted(Guid sipProviderId)
        {
            Persistor.SIPProviderExternalDeleted(sipProviderId);
        }

        /// <summary>
        /// Informs that hosting agent that a SIPProvider has been updated. This method is redundant when an XML persistence layer is in use.
        /// </summary>
        /// <param name="sipProviderId">The id of the updated provider.</param>
        [OperationContract]
        public void SIPProviderUpdated(Guid sipProviderId)
        {
            Persistor.SIPProviderExternalUpdated(sipProviderId);
        }

        /// <summary>
        /// Informs that hosting agent that a SIPProvider has been added. This method is redundant when an XML persistence layer is in use.
        /// </summary>
        /// <param name="sipProviderId">The id of the added provider.</param>
        [OperationContract]
        public void SIPProviderAdded(Guid sipProviderId)
        {
            Persistor.SIPProviderExternalAdded(sipProviderId);
        }
    }
}

