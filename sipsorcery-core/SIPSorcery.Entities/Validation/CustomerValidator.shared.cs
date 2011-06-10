using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace SIPSorcery.Entities
{
    public class CustomerValidator
    {
        public static ValidationResult IsValid(Customer customer, ValidationContext context)
        {
            if (customer.ID == Guid.Empty.ToString() && customer.CustomerPassword != customer.RetypedPassword)
            {
                return new ValidationResult("The password and retyped password were not the same.", new String[] { "CustomerPassword", "RetypedPassword" });
            }

            return ValidationResult.Success;
        }
    }
}
