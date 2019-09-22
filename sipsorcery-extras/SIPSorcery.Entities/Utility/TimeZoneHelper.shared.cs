//-----------------------------------------------------------------------------
// Filename: TimeZoneHelper.cs
//
// Description:
// Helper methods for dealing with timezones. 
// 
// History:
// 30 Apr 2011	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty. Ltd., Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty. Ltd. 
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
using System.Text;

namespace SIPSorcery.Entities
{
    public class TimeZoneHelper
    {
        /// <summary>
        /// Applies a minutes offset to a date time offset.
        /// </summary>
        /// <param name="dateTimeOffsetStr">Must be a valid DateTimeOffset. Typically this calue should be coming from the database.</param>
        /// <param name="offset">The offset in minutes to apply to the time string.</param>
        /// <returns>A date time object representing the parsed dateTimeOffsetStr string with the offset applied.</returns>
        public static DateTime ApplyOffset(string dateTimeOffsetStr, int offset)
        {
            DateTimeOffset dateTimeOffset = DateTimeOffset.MinValue;
            if (DateTimeOffset.TryParse(dateTimeOffsetStr, out dateTimeOffset))
            {
                dateTimeOffset = dateTimeOffset.AddMinutes(offset);
            }
            return dateTimeOffset.DateTime;
        }

#if !SILVERLIGHT

        /// <summary>
        /// Returns the offset minutes from UTC time for a timezone. This method is typically used to convert times that are stored in the
        /// database as UTC time to a user's local time.
        /// </summary>
        /// <param name="timezoneStr">The timezone to get the offset from UTC for.</param>
        /// <returns>If the supplied timezone string is recognised then the number of minutes difference between it and UTC otherwise 0.</returns>
        public static int GetTimeZonesUTCOffsetMinutes(string timezoneStr)
        {
            if (timezoneStr == null)
            {
                return 0;
            }

            foreach (TimeZoneInfo timezone in TimeZoneInfo.GetSystemTimeZones())
            {
                if (timezone.DisplayName == timezoneStr || timezone.DisplayName.Replace("UTC", "GMT") == timezoneStr)
                {
                    return (int)timezone.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
                }
            }

            return 0;
        }

#endif

    }
}
