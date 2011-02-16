// ============================================================================
// FileName: DialPlanSettings.cs
//
// Description:
// This class represents settings that are passed to a dial plan that is intended to operate
// with the dial plan wizard Ruby script. The settings are configured through a custom designed
// user interface.
//
// Author(s):
// Aaron Clauson
//
// History:
// 08 Feb 2011  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Text.RegularExpressions;
using SIPSorcery.SIP.App;
using SIPSorcery.SIP.App.Entities;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class DialPlanSettings
    {
        private static ILog logger = AppState.logger;

        public List<SIPDialplanLookup> Lookups;
        public List<SIPDialplanRoute> Routes;
        public Dictionary<string, SIPDialplanProvider> Providers;
        public SIPDialplanOption Options;

        public DialPlanSettings(List<SIPDialplanLookup> lookups,
            List<SIPDialplanRoute> routes,
            Dictionary<string, SIPDialplanProvider> providers,
            SIPDialplanOption options)
        {
            Lookups = lookups;
            Routes = routes;
            Providers = providers;
            Options = options;
        }
        
        public Dictionary<string, string> GetSpeedDials()
        {
            return GetLookups(SIPDialPlanLookupTypes.SpeedDial);
        }

        public Dictionary<string, string> GetCNAMs()
        {
            return GetLookups(SIPDialPlanLookupTypes.CNAM);
        }

        public Dictionary<string, string> GetENUMs()
        {
            return GetLookups(SIPDialPlanLookupTypes.ENUM);
        }

        private Dictionary<string, string> GetLookups(SIPDialPlanLookupTypes lookupType)
        {
            try
            {
                if (Lookups != null)
                {
                    var lookups = (from lookup in Lookups
                                   where lookup.lookuptype == (int)lookupType
                                   select lookup).ToList();

                    if (lookups != null && lookups.Count > 0)
                    {
                        Dictionary<string, string> lookupsTable = new Dictionary<string, string>();

                        foreach (SIPDialplanLookup lookup in lookups)
                        {
                            lookupsTable.Add(lookup.lookupkey, lookup.lookupvalue);
                        }

                        return lookupsTable;
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetLookups. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// If set retrieves the user's UTC time zone offset for use in the dialplan.
        /// </summary>
        /// <returns>An integer that represent the offset from UTC for the user's timezone. If no timezone
        /// is set 0 is returned meaning any time calculations will assume UTC.</returns>
        public int GetTimezoneOffset()
        {
            if (Options != null && !Options.timezone.IsNullOrBlank())
            {
                foreach (TimeZoneInfo timezone in TimeZoneInfo.GetSystemTimeZones())
                {
                    if (timezone.DisplayName == Options.timezone)
                    {
                        return (int)timezone.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets a list of any excluded prefixes the user has specified in their dialplan options. The
        /// excluded prefix strings will be stored in the database as a CRLF separated list of strings.
        /// Spaces are used in excluded prefixes so they should not be trimmed.
        /// </summary>
        /// <returns>If available a list of ENUM servers or null if none have been specified.</returns>
        public List<string> GetExcludedPrefixes()
        {
            if (!Options.excludedprefixes.IsNullOrBlank())
            {
                string[] excludedPrefixes = Regex.Split(Options.excludedprefixes, @"\r\n", RegexOptions.Multiline);
                return excludedPrefixes.Where(x => !x.Trim().IsNullOrBlank()).ToList();
            }

            return null;
        }

        /// <summary>
        /// Gets a list of any ENUM servers the user has specified in their dialplan options. The ENUM
        /// servers will be stored in the database as a CRLF separated list of strings.
        /// </summary>
        /// <returns>If available a list of ENUM servers or null if none have been specified.</returns>
        public List<string> GetENUMServers()
        {
            if (!Options.enumservers.IsNullOrBlank())
            {
                string[] enumServers = Regex.Split(Options.enumservers, @"\r\n", RegexOptions.Multiline);
                return enumServers.Where(x => !x.Trim().IsNullOrBlank()).Select(x => x.Trim()).ToList();
            }

            return null;
        }

        /// <summary>
        /// Gets a list of allowed country codes that the user has specified in their dialplan options.
        /// </summary>
        /// <returns>A list of strings that represent country codes for white listed call destinations.</returns>
        public List<string> GetAllowedCountries()
        {
            if (!Options.allowedcountrycodes.IsNullOrBlank())
            {
                string[] allowedCodes = Regex.Split(Options.allowedcountrycodes, @"\s+", RegexOptions.Multiline);
                return allowedCodes.Where(x => !x.Trim().IsNullOrBlank()).Select(x => x.Trim()).ToList();
            }

            return null;
        }
    }
}
