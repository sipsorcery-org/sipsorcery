//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Main program for SIP/Web server application. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// insert into Sipdomains values (newid(), 'aspnet.sipsorcery.com', null, null, '2020-12-29T00:00:00.0000000+00:00');
// curl -X POST "https://localhost:5001/api/SIPAccounts" -H "accept: text/plain" -H  "Content-Type: application/json" -d "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"sipUsername\":\"aaron\",\"sipPassword\":\"password\",\"owner\":\"\",\"Sipdomain\":\"aspnet.sipsorcery.com\",\"isDisabled\":false,\"inserted\":\"2020-12-29T00:00:00.0000000+00:00\"}"
//-----------------------------------------------------------------------------

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace SIPAspNetServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

            var factory = new SerilogLoggerFactory(Log.Logger);
            SIPSorcery.LogFactory.Set(factory);

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();

                    //if (hostContext.HostingEnvironment.IsDevelopment())
                    //{
                    //    webBuilder.AddUserSecrets<Program>();
                    //}
                });
    }
}
