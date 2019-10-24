//-----------------------------------------------------------------------------
// Filename: RTPPayloadTypes.cs
//
// Description: Stuctures and helper functions for RTP Payload types.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 May 2005	Aaron Clauson	Created (aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com).
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
		GSM = 3,
        Dynamic = 96,
	}

	public class RTPPayloadTypes
	{
		public RTPPayloadTypes()
		{

		}
	}
}
