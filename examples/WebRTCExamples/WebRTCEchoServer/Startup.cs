//-----------------------------------------------------------------------------
// Filename: Startup.cs
//
// Description: Minimal ASP.NET Core initialisation for a WebRTC Echo Test service.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 10 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace demo
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy(name: "default",
                    builder =>
                    {
                        builder.WithOrigins("*")
                               .AllowAnyHeader()
                                .AllowAnyMethod();
                    });
            });

            services.AddTransient<WebRTCEchoServer>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseCors();
            app.UseStaticFiles();
            app.UseRouting();

            SIPSorcery.LogFactory.Set(app.ApplicationServices.GetService<ILoggerFactory>());

            var options = new RewriteOptions()
                   .AddRedirect("^[/]?$", "index.html");
            app.UseRewriter(options);

            app.UseEndpoints(endpoints =>
            {
                var jsonOptions = new JsonSerializerOptions();
                jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

                endpoints.MapPost("/offer", async context =>
                {
                    var id = context.Request.RouteValues["id"] as string;
                    var offer = await JsonSerializer.DeserializeAsync<RTCSessionDescriptionInit>(context.Request.Body, jsonOptions);

                    if (offer != null && offer.type == RTCSdpType.offer)
                    {
                        var echoSvc = app.ApplicationServices.GetService<WebRTCEchoServer>();

                        var answer = await echoSvc.GotOffer(offer);
                        if (answer != null)
                        {
                            await JsonSerializer.SerializeAsync(context.Response.Body, answer, jsonOptions);
                        }
                        else
                        {
                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            await context.Response.WriteAsync("Failed to get an answer.");
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("Invalid offer.");
                    }
                });
            });
        }
    }
}
