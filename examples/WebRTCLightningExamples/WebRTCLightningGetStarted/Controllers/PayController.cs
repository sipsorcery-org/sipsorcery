//-----------------------------------------------------------------------------
// Filename: PayController.cs
// 
// Description: A web API controller that was used to test the payments mechanism
// before the Lightning listener was wired up.
// 
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 01 Mar 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace demo;

[ApiController]
[Route("pay")]
public class PayController : ControllerBase
{
    private readonly ILogger<PayController> _logger;

    public PayController(
        ILogger<PayController> logger)
    {
        _logger = logger;
    }

    //[HttpGet("{id}")]
    //public IActionResult Get(string id)
    //{
    //    _logger.LogDebug($"pay id={id}");

    //    return _peerConnectionPayState.TrySetPaid(id) ?
    //        Ok() : BadRequest();
    //}

    //[HttpGet("list")]
    //public string List()
    //{
    //    _logger.LogDebug("pay/list");

    //    //return WebRtcDaemon.GetInstance().List();

    //    return string.Empty;
    //}
}
