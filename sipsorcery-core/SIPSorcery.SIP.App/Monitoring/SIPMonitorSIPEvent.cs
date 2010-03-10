// ============================================================================
// FileName: SIPMonitorSIPEvent.cs
//
// Description:
// Describes the types of events that can be sent by the different SIP Servers to SIP
// Monitor clients.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Feb 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com)
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
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.SIP.App
{
    public class SIPMonitorSIPEvent : SIPMonitorEvent
    {
        public const string SERIALISATION_PREFIX = "3";     // Prefix appended to the front of a serialised event to identify the type.

        public SIPEventPackage EventPackage;

        private SIPMonitorSIPEvent()
        {
            m_serialisationPrefix = SERIALISATION_PREFIX;
            ClientType = SIPMonitorClientTypesEnum.SIPEvent;
        }

        public SIPMonitorSIPEvent(SIPEventPackage eventPackage, string username, SIPEndPoint remoteEndPoint, string message)
        {
            EventPackage = eventPackage;
            Username = username;
            RemoteEndPoint = remoteEndPoint;
            Message = message;
        }

        public static SIPMonitorSIPEvent ParseSIPEventCSV(string eventCSV)
        {
            try
            {
                SIPMonitorSIPEvent sipEvent = new SIPMonitorSIPEvent();

                if (eventCSV.IndexOf(END_MESSAGE_DELIMITER) != -1)
                {
                    eventCSV.Remove(eventCSV.Length - 2, 2);
                }

                string[] eventFields = eventCSV.Split(new char[] { '|' });

                sipEvent.EventPackage = SIPEventPackage.Parse(eventFields[1]);
                sipEvent.Username = eventFields[2];
                sipEvent.RemoteEndPoint = SIPEndPoint.ParseSIPEndPoint(eventFields[3]);
                sipEvent.Message = eventFields[4].Trim('#');

                return sipEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorSIPEvent ParseEventCSV. " + excp.Message);
                return null;
            }
        }

        public override string ToCSV()
        {
            try
            {
                string remoteSocket = (RemoteEndPoint != null) ? RemoteEndPoint.ToString() : null;

                string csvEvent =
                    SERIALISATION_PREFIX + "|" +
                    EventPackage.ToString() + "|" +
                    Username + "|" +
                    remoteSocket + "|" +
                    Message + END_MESSAGE_DELIMITER;

                return csvEvent;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorSIPEvent ToCSV. " + excp.Message);
                return null;
            }
        }
    }
}
