using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Web;

namespace SIPSorcery.Web.Services
{
    public class JSONResult<T>
    {
        [DataMember] public bool Success { get; set; }
        [DataMember] public string Error { get; set; }
        [DataMember] public T Result { get; set; }
    }
}