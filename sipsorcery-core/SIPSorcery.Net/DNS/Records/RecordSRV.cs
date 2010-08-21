// ============================================================================
// FileName: RecordSRV.cs
//
// Description:
// 
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Feb 2009	Aaron Clauson   Created.
//
// License:
// http://www.opensource.org/licenses/gpl-license.php
// ===========================================================================

using System;
using System.Collections.Generic;

#region RFC Specifications
/*
    
 
 */
#endregion

namespace Heijden.DNS
{
    public class RecordSRV : Record
	{		
        public ushort Priority;
		public ushort Weight;
		public ushort Port;
        public string Target;

        public RecordSRV(RecordReader rr)
        {
            Priority = rr.ReadShort();
            Weight = rr.ReadShort();
            Port = rr.ReadShort();
            Target = rr.ReadDomainName();
 		}

		public override string ToString()
		{
			return string.Format("{0} {1} {2} {3}",
                Priority,
                Weight,
                Port,
                Target);
		}
	}
}
