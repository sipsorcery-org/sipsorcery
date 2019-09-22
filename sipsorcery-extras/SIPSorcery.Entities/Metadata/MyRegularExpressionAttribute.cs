//-----------------------------------------------------------------------------
// Filename: MyRegularExpressionAttribute.cs
//
// Description:
// Workaround for Microsoft bug with the RegularExpressionAttribute. Can be removed when fixed.
// https://connect.microsoft.com/VisualStudio/feedback/details/1988437/generated-code-for-silverlight-references-matchtimeoutinmilliseconds-which-does-not-exist
// 
// History:
// 14 Feb 2016	Aaron Clauson	    Created.

using System.ComponentModel.DataAnnotations;

namespace SIPSorcery.Entities
{
    public class MyRegularExpressionAttribute : RegularExpressionAttribute
    {
        public MyRegularExpressionAttribute(string pattern) : base(pattern) { }

        protected new int MatchTimeoutInMilliseconds { get; set; }
    }
}
