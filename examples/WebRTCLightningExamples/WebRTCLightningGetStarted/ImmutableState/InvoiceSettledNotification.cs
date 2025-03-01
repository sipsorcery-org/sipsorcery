//-----------------------------------------------------------------------------
// Filename: InvoiceSettledNotification.cs
//
// Description: Event notification for a Lightning invoice settled event.
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

public record InvoiceSettledNotification(string RHash, string Description);

