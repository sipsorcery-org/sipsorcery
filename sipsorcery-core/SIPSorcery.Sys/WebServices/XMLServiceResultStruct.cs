//-----------------------------------------------------------------------------
// Filename: XMLServiceResultStruct
//
// Description: 
// A wrapper around a block of XML that is being returned to a caller that adds
// a standardised way of including timestamps and a status or error message.
//
// History:
// 15 Aug 2006	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace SIPSorcery.Sys
{
    [DataContract]
    public struct XMLServiceResultStruct
	{
        [DataMember]
        public bool Error;

        [DataMember]
		public string Message;

        [DataMember]
		public string ResultXML;

		public XMLServiceResultStruct(bool error, string message, string resultXML)
		{
			Error = error;
			Message = message;
			ResultXML = resultXML;
		}

        public new string ToString()
        {
            string pageXML = null;

            if (!Error)
            {
                pageXML = "<result timestamp='" + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + "'>" + SafeXML.MakeSafeXML(Message) + "</result>" + ResultXML;
            }
            else
            {
                pageXML = "<error timestamp='" + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + "'>" + SafeXML.MakeSafeXML(Message) + "</error>";
            }

            return pageXML;
        }
        
        /// <summary>
        /// When not called from a MaverickLite controller the <page> root element will not be added automatically. This method compensates
        /// </summary>
        /// <returns></returns>
        public string ToStringWithRoot()
        {
            return "<page>" + ToString() + "</page>";
        }
	}
}
