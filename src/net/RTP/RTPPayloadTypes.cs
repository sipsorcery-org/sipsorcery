//-----------------------------------------------------------------------------
// Filename: RTPPayloadTypes.cs
//
// Description: Stuctures and helper functions for RTP Payload types.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 23 May 2005	Aaron Clauson	Created, Montreux, Switzerland.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.Net
{
	public enum RTPPayloadTypesEnum
	{
		PCMU = 0,
        PCMA = 1,
        Dynamic = 96,
	}

	public class RTPPayloadTypes
	{
        /// <summary>
        /// Attempts to get the sampling frequency of a payload type.
        /// </summary>
        /// <param name="payloadType">The payload type to get the frequency for.</param>
        /// <returns>An integer representing the payload type's sampling frequency or 0 if it's
        /// dynamic or can't be determined.</returns>
		public static int GetSamplingFrequency(RTPPayloadTypesEnum payloadType)
		{
            switch(payloadType)
            {
                case RTPPayloadTypesEnum.PCMU:
                case RTPPayloadTypesEnum.PCMA:
                    return 8000;
                default:
                    return 0;
            }
		}
	}
}
