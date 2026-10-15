//-----------------------------------------------------------------------------
// Filename: SIPSoftPhoneState.cs
//
// Description: A helper class to load the application's settings and to hold 
// some application wide variables. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Mar 2012	Aaron Clauson	Refactored, Hobart, Australia.
// 15 Sep 2026	Aaron Clauson	Switched from App.config to appsettings.json.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Extensions.Logging;

namespace SIPSorcery.SoftPhone
{
    public static class SIPSoftPhoneState
    {
        private const string APPSETTINGS_FILENAME = "appsettings.json";
        private const string ENVIRONMENT_VARIABLE_PREFIX = "SOFTPHONE_";
        private const string ENVIRONMENT_VARIABLE_NAME = "DOTNET_ENVIRONMENT";
        private const string DEVELOPMENT_ENVIRONMENT = "Development";
        private const string PRODUCTION_ENVIRONMENT = "Production";

        /// <summary>
        /// The environment name used to select the appsettings.{Environment}.json file. Taken from
        /// the DOTNET_ENVIRONMENT environment variable and if that's not set defaults to
        /// Development for a Debug build and Production for a Release build.
        /// </summary>
        public static readonly string EnvironmentName;

        /// <summary>
        /// The raw configuration. Useful if a setting needs to be read that's not on
        /// the <see cref="Settings"/> object.
        /// </summary>
        public static readonly IConfiguration Configuration;

        /// <summary>
        /// The application's settings as loaded at start up.
        /// </summary>
        public static readonly SoftphoneSettings Settings;

        public static IPAddress PublicIPAddress;

        static SIPSoftPhoneState()
        {
            AddDebugLogger();

            EnvironmentName = Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE_NAME);
            if (string.IsNullOrWhiteSpace(EnvironmentName))
            {
#if DEBUG
                EnvironmentName = DEVELOPMENT_ENVIRONMENT;
#else
                EnvironmentName = PRODUCTION_ENVIRONMENT;
#endif
            }

            // The settings files are copied beside the executable so the base path needs to be
            // the application's directory rather than the current working directory. Each source
            // overrides the ones before it.
            Configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(APPSETTINGS_FILENAME, optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{EnvironmentName}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(ENVIRONMENT_VARIABLE_PREFIX)
                .Build();

            Settings = Configuration.GetSection(SoftphoneSettings.SectionName).Get<SoftphoneSettings>()
                ?? new SoftphoneSettings();
        }

        private static void AddDebugLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Debug()
                .CreateLogger();
            SIPSorcery.LogFactory.Set(new SerilogLoggerFactory(serilogLogger));
        }
    }
}
