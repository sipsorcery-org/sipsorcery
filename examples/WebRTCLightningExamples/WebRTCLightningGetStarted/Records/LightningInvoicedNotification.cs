//-----------------------------------------------------------------------------
// Filename: LightningInvoicedNotification.cs
//
// Description: Event notification for a Lightning invoice events.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace demo;

public enum LightningInvoiceNotificationTypeEnum
{
    Cancelled,
    Settled
}

public record LightningInvoicedNotification(LightningInvoiceNotificationTypeEnum NotificationType, string RHash, string Description);
