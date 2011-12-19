using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.Entities;
using System.Web;

namespace SIPSorcery.Web.Services
{
    public class PaymentRequiredException : ApplicationException
    {
        public CustomerServiceLevels ServiceLevel { get; private set; }

        public PaymentRequiredException(CustomerServiceLevels serviceLevel, string message)
            : base(message)
        {
            ServiceLevel = serviceLevel;
        }
    }
}