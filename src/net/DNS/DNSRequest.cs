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
// 28 Mar 2008	Aaron Clauson   Changed class name from Request to DNSRequest to avoid confusion.
// 14 Oct 2019  Aaron Clauson   Synchronised with latest version of source from at https://www.codeproject.com/Articles/23673/DNS-NET-Resolver-C.
//
// License:
// The Code Project Open License (CPOL) https://www.codeproject.com/info/cpol10.aspx
// ============================================================================

using System.Collections.Generic;

namespace Heijden.DNS
{
	public class DNSRequest
	{
		public Header header;

		public List<Question> Questions;

		public DNSRequest()
		{
			header = new Header();
			header.OPCODE = OPCode.Query;
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
