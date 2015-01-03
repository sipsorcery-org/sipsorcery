//-----------------------------------------------------------------------------
// Filename: RTPPayloadTypes.cs
//
// Description: Stuctures and helper functions for RTP Payload types.
//
// History:
// 23 May 2005	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;

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
