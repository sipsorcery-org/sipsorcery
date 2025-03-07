//-----------------------------------------------------------------------------
// Filename: PaymentStateTrigger.cs
//
// Description: The payment state triggers for the payment state machine.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Mar 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace demo;

public enum PaymentStateTrigger
{
    Tick,
    InvoicePaid,
    InvoiceExpired
}
