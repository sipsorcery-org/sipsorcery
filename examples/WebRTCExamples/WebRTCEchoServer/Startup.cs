using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

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
                var echoSvc = new WebRTCEchoService(
                    app.ApplicationServices.GetService<ILoggerFactory>(),
                    app.ApplicationServices.GetService<IConfiguration>());

                endpoints.MapGet("/offer/{id}", async context =>
                {
                    var id = context.Request.RouteValues["id"] as string;
                    if (echoSvc.IsInUse(id))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("The provided id is already in use. Please try a different one.");
                    }
                    else
                    {
                        var offer = await echoSvc.GetOffer(id);
                        await JsonSerializer.SerializeAsync(context.Response.Body, offer, jsonOptions);
                    }
                });

                endpoints.MapPost("/answer/{id}", async context =>
                {
                    var id = context.Request.RouteValues["id"] as string;
                    if (!echoSvc.IsInUse(id))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("The provided id did not match an existing peer connection. Please get an offer first.");
                    }
                    else
                    {
                        var answer = await JsonSerializer.DeserializeAsync<RTCSessionDescriptionInit>(context.Request.Body, jsonOptions);
                        echoSvc.SetRemoteDescription(id, answer);
                        await context.Response.CompleteAsync();
                    }
                });

                endpoints.MapPost("/ice/{id}", async context =>
                {
                    var id = context.Request.RouteValues["id"] as string;
                    if (!echoSvc.IsInUse(id))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("The provided id did not match an existing peer connection. Please get an offer first.");
                    }
                    else
                    {
                        var candidate = await JsonSerializer.DeserializeAsync<RTCIceCandidateInit>(context.Request.Body, jsonOptions);
                        echoSvc.AddIceCandidate(id, candidate);
                        await context.Response.CompleteAsync();
                    }
                });
            });
        }
    }
}
