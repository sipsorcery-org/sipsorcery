//-----------------------------------------------------------------------------
// Filename: WebRTCController.cs
//
// Description: ASP.Net Web API controller for WebRTC example. This
// controller's purpose is to accept requests from the client and
// pass them through to the singleton WebRTC service.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 13 May 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace WebRTCAspNetMvc.Controllers
{
    [Route("api/webrtc")]
    [ApiController]
    public class WebRTCController : ControllerBase
    {
        private readonly ILogger<WebRTCController> _logger;
        private readonly WebRTCHostedService _webRTCServer;

        public WebRTCController(ILogger<WebRTCController> logger, WebRTCHostedService webRTCServer)
        {
            _logger = logger;
            _webRTCServer = webRTCServer;
        }

        [HttpGet]
        [Route("getoffer")]
        public async Task<IActionResult> GetOffer(string id)
        {
            _logger.LogDebug($"WebRTCController GetOffer {id}.");
            return Ok(await _webRTCServer.GetOffer(id));
        }

        [HttpPost]
        [Route("setanswer")]
        public IActionResult SetAnswer(string id, [FromBody] RTCSessionDescriptionInit answer)
        {
            _logger.LogDebug($"SetAnswer {id} {answer?.type} {answer?.sdp}.");

            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("The id cannot be empty in SetAnswer.");
            }
            else if (string.IsNullOrWhiteSpace(answer?.sdp))
            {
                return BadRequest("The SDP answer cannot be empty in SetAnswer.");
            }

            _webRTCServer.SetRemoteDescription(id, answer);
            return Ok();
        }

        [HttpPost]
        [Route("addicecandidate")]
        public IActionResult AddIceCandidate(string id, [FromBody] RTCIceCandidateInit iceCandidate)
        {
            _logger.LogDebug($"SetIceCandidate {id} {iceCandidate?.candidate}.");

            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("The id cannot be empty in AddIceCandidate.");
            }
            else if (string.IsNullOrWhiteSpace(iceCandidate?.candidate))
            {
                return BadRequest("The candidate field cannot be empty in AddIceCandidate.");
            }

            _webRTCServer.AddIceCandidate(id, iceCandidate);

            return Ok();
        }
    }
}