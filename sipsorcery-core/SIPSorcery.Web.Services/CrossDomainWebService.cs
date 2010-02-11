//-----------------------------------------------------------------------------
// Filename: CrossDomainWebService.cs
//
// Description: Returns the client policy access files requested by Silverlight clients. Without the policy
// file the Silverlight client will not allow cross-domain web service calls.
// 
// History:
// 08 Oct 2008	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.ServiceModel.Web;
using System.Text;
using System.Data;
using System.IO;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    [ServiceContract(Namespace="http://www.sipsorcery.com")]
    public interface ICrossDomain
    {
        // For some inexplicable reason returning this file to Silverlight resulted in an authorisation failure.
        // The file format was quadruple checked but to no avail. The flash format policy file worked perfectly 
        // so this one was purposefully removed.
        //[OperationContract, WebGet(UriTemplate = "/clientaccesspolicy.xml")]
        //Stream GetSilverlightPolicy();

        [OperationContract, WebGet(UriTemplate = "/crossdomain.xml")]
        Stream GetFlashPolicy();

        [OperationContract, WebGet(UriTemplate = "/sipsorcery.html")]
        Stream GetSIPSorceryHTML();

        [OperationContract, WebGet(UriTemplate = "/clientbin/sipsorcery.xap")]
        Stream GetSIPSorceryXAP();
    }

    public class CrossDomainService : ICrossDomain
    {
        private const string HTTP_SERVER_BASE_DIR_KEY = "HTTPServerBaseDirectory";

        private ILog logger = log4net.LogManager.GetLogger("websvc-crossdomain");

        private static readonly string m_baseDirectory;

        static CrossDomainService()
        {
            m_baseDirectory = AppState.ToAbsoluteDirectoryPath(AppState.GetConfigSetting(HTTP_SERVER_BASE_DIR_KEY));

            if (m_baseDirectory.IsNullOrBlank())
            {
                m_baseDirectory = AppState.CurrentDirectory + @"\";
            }
        }

        private Stream StringToStream(string result)
        {
            WebOperationContext.Current.OutgoingResponse.ContentType = "application/xml";
            return new MemoryStream(Encoding.UTF8.GetBytes(result));
        }

        public Stream GetSilverlightPolicy()
        {
            logger.Debug("Request for clientaccesspolicy.xml.");

            string policy = @"<?xml version=""1.0"" encoding=""utf-8""?>
<access-policy>
    <cross-domain-access>
        <policy>
            <allow-from>
                <domain uri=""*""/>
            </allow-from>
            <grant-to>
                <resource path=""/"" include-subpaths=""true""/>
            </grant-to>
        </policy>
    </cross-domain-access>
</access-policy>"; 

            return StringToStream(policy);
        }

        public Stream GetFlashPolicy()
        {
            logger.Debug("Request for crossdomain.xml.");

            string result = @"<?xml version=""1.0""?>
<!DOCTYPE cross-domain-policy SYSTEM ""http://www.macromedia.com/xml/dtds/cross-domain-policy.dtd"">
<cross-domain-policy>
   <allow-http-request-headers-from domain=""*"" headers=""*""/>
</cross-domain-policy>";
            return StringToStream(result);
        }

        public Stream GetSIPSorceryHTML()
        {
            logger.Debug("Request for sipsorcery.html.");

            if (File.Exists(m_baseDirectory + "sipsorcery.html"))
            {
                StreamReader sr = new StreamReader(m_baseDirectory + "sipsorcery.html");
                return sr.BaseStream;
            }
            else
            {
                logger.Error("File sipsorcery.html did not exist in " + m_baseDirectory + ".");
                return null;
            }
        }

        public Stream GetSIPSorceryXAP()
        {
            logger.Debug("Request for clientbin/sipsorcery.xap.");

            if (File.Exists(m_baseDirectory + "clientbin/sipsorcery.xap"))
            {
                StreamReader sr = new StreamReader(m_baseDirectory + "clientbin/sipsorcery.xap");
                return sr.BaseStream;
            }
            else
            {
                logger.Error("File clientbin/sipsorcery.xap did not exist in " + m_baseDirectory + ".");
                return null;
            }
        }
    }
}

