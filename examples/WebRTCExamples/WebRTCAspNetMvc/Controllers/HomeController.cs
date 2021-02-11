//-----------------------------------------------------------------------------
// Filename: HomeController.cs
//
// Description: Default ASP.Net MVC controller for WebRTC example. This
// controller's purpose is to serve up the HTML page with the WebRTC client
// side script.
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

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WebRTCAspNetMvc.Models;

namespace WebRTCAspNetMvc.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
