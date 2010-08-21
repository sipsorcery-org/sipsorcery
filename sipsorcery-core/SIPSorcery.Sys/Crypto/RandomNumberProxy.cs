//-----------------------------------------------------------------------------
// Filename: RandomNumberProxy.cs
//
// Description: Calls a web service at random.org to get a random number seed.
//
// History:
// 23 Apr 2006	Aaron Clauson	Created.
//
// License:
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using System.Web.Services;
using System.Web.Services.Protocols;
using System.Xml.Serialization;
using log4net;

namespace Aza.Configuration
{
	[System.Web.Services.WebServiceBindingAttribute(Name="RandomDotOrgBinding", Namespace="http://www.random.org/RandomDotOrg.wsdl")]
	public class RandomNumberSeedProxy : System.Web.Services.Protocols.SoapHttpClientProtocol 
	{	
		private static ILog logger = AppState.logger;
		
		/// <remarks/>
		public RandomNumberSeedProxy() 
		{
			this.Url = "http://www.random.org/cgi-bin/Random.cgi";
		}

		[System.Web.Services.Protocols.SoapRpcMethodAttribute("", RequestNamespace="RandomDotOrg", ResponseNamespace="RandomDotOrg")]
		[return: System.Xml.Serialization.SoapElementAttribute("return")]
		public int lrand48() 
		{
			object[] results = this.Invoke("lrand48", new object[0]);
			return ((int)(results[0]));
		}

		[System.Web.Services.Protocols.SoapRpcMethodAttribute("", RequestNamespace="RandomDotOrg", ResponseNamespace="RandomDotOrg")]
		[return: System.Xml.Serialization.SoapElementAttribute("return")]
		public long mrand48() 
		{
			object[] results = this.Invoke("mrand48", new object[0]);
			return ((long)(results[0]));
		}
	}
}
