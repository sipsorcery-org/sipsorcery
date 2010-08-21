// ============================================================================
// FileName: DNSRequest.cs
//
// Description:
// 
//
// Author(s):
// Alphons van der Heijden
//
// History:
// 28 Mar 2008	Aaron Clauson   Added to sipwitch code base based on http://www.codeproject.com/KB/library/DNS.NET_Resolver.aspx.
// 28 Mar 2008	Aaron Clauson   Changed class name from Request to DNSRequet to avoid confusion.
//
// License:
// http://www.opensource.org/licenses/gpl-license.php
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace Heijden.DNS
{
	public class DNSRequest
	{
		public Header header;

		public List<Question> Questions;

		public DNSRequest()
		{
			header = new Header();
			header.OPCODE = OPCode.QUERY;
			header.QDCOUNT = 0;

			Questions = new List<Question>();
		}

		public void AddQuestion(Question question)
		{
			Questions.Add(question);
		}

		public byte[] Data
		{
			get
			{
				List<byte> data = new List<byte>();
				header.QDCOUNT = (ushort)Questions.Count;
				data.AddRange(header.Data);
				foreach (Question q in Questions)
					data.AddRange(q.Data);
				return data.ToArray();
			}
		}
	}
}
