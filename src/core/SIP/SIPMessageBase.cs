//-----------------------------------------------------------------------------
// Filename: SIPMessageBase.cs
//
// Description: Common base class for SIPRequest and SIPResponse classes.
//
// Author(s):
// Salih YILDIRIM (github: salihy)
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Nov 2019	Salih YILDIRIM  Created.
// 26 Nov 2019  Aaron Clauson   Converted from interface to base class to extract common properties.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPMessageBase
    {
        protected static ILogger logger = Log.Logger;

        protected const string m_CRLF = SIPConstants.CRLF;
        protected const string m_sipFullVersion = SIPConstants.SIP_FULLVERSION_STRING;
        protected const string m_allowedSIPMethods = SIPConstants.ALLOWED_SIP_METHODS;

        /// <summary>
        /// The SIP request/response's headers collection.
        /// </summary>
        public SIPHeader Header;

        /// <summary>
        /// The optional body or payload for the SIP request/response.
        /// </summary>
        public string Body;

        /// <summary>
        /// Timestamp for the SIP request/response's creation.
        /// </summary>
        public DateTime Created = DateTime.Now;

        /// <summary>
        /// The remote SIP socket the request/response was received from.
        /// </summary>
        public SIPEndPoint RemoteSIPEndPoint { get; protected set; }

        /// <summary>
        /// The local SIP socket the request/response was received on.
        /// </summary>
        public SIPEndPoint LocalSIPEndPoint { get; protected set; }

        /// <summary>
        /// When the SIP transport layer has multiple channels it will use this ID hint to choose amongst them when 
        /// sending this request/response.
        /// </summary>
        public string SendFromHintChannelID;

        /// <summary>
        /// For connection oriented SIP transport channels this ID provides a hint about the specific connection to use
        /// when sending this request/response.
        /// </summary>
        public string SendFromHintConnectionID;
    }
}
