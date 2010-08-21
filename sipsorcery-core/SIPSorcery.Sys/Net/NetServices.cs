// ============================================================================
// FileName: NetServices.cs
//
// Description:
// Contains wrappers to access the functionality of the underlying operating
// system.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Dec 2005	Aaron Clauson	Created.
// ============================================================================

using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{
    public enum PlatformEnum
    {
        Windows = 1,
        Linux = 2,
    }
    
    public class NetServices
	{
        public const int UDP_PORT_START = 1025;
        public const int UDP_PORT_END = 65535;

        public static PlatformEnum Platform = PlatformEnum.Windows;

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, out IPEndPoint localEndPoint)
        {
            return CreateRandomUDPListener(localAddress, UDP_PORT_START, UDP_PORT_END, null, out localEndPoint);
        }

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, int start, int end, ArrayList inUsePorts, out IPEndPoint localEndPoint)
        {
            try
            {
                UdpClient randomClient = null;
                int attempts = 1;

                localEndPoint = null;

                while (attempts < 50)
                {
                    int port = Crypto.GetRandomInt(start, end);
                    if (inUsePorts == null || !inUsePorts.Contains(port))
                    {
                        try
                        {
                            localEndPoint = new IPEndPoint(localAddress, port);
                            randomClient = new UdpClient(localEndPoint);
                            break;
                        }
                        catch
                        {
                            //logger.Warn("Warning couldn't create UDP end point for " + localAddress + ":" + port + "." + excp.Message);
                        }

                        attempts++;
                    }
                }

                //logger.Debug("Attempts to create UDP end point for " + localAddress + ":" + port + " was " + attempts);

                return randomClient;
            }
            catch
            {
                throw new ApplicationException("Unable to create a random UDP listener between " + start + " and " + end);
            }
        }

        /// <summary>
        /// Extracts the default gateway from the route print command
        /// </summary>
        /// <returns>The IP Address of the default gateway.</returns>
        public static IPAddress GetDefaultGateway()
        {
            try
            {
                string routeTable = CallRoute();

                if (routeTable != null)
                {
                    if (Platform == PlatformEnum.Windows)
                    {
                        Match gatewayMatch = Regex.Match(routeTable, @"Gateway\s*:\s*(?<gateway>(\d+\.){3}\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                        if (gatewayMatch.Success)
                        {
                            return IPAddress.Parse(gatewayMatch.Result("${gateway}"));
                        }
                    }
                    else
                    {
                        Match gatewayMatch = Regex.Match(routeTable, @"default\s*(?<gateway>(\d+\.){3}\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                        if (gatewayMatch.Success)
                        {
                            return IPAddress.Parse(gatewayMatch.Result("${gateway}"));
                        }
                    }
                }

                return null;
            }
            catch
            {
                //logger.Error("Exception GetDefaultGateway. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Attempts to get the local IP address that is being used with the deault gatewya and is therefore the one being used
        /// to connect to the internet.
        /// </summary>
        /// <param name="defaultGateway"></param>
        /// <returns></returns>
        public static IPAddress GetDefaultIPAddress(IPAddress defaultGateway)
        {

            try
            {
                string[] gatewayOctets = Regex.Split(defaultGateway.ToString(), @"\.");

                IPHostEntry hostEntry = Dns.Resolve(Dns.GetHostName());

                ArrayList possibleMatches = new ArrayList();
                foreach (IPAddress localAddress in hostEntry.AddressList)
                {
                    possibleMatches.Add(localAddress);
                }

                for (int octetIndex = 0; octetIndex < 4; octetIndex++)
                {
                    IPAddress[] testAddresses = (IPAddress[])possibleMatches.ToArray(typeof(IPAddress));
                    foreach (IPAddress localAddress in testAddresses)
                    {
                        string[] localOctets = Regex.Split(localAddress.ToString(), @"\.");
                        if (gatewayOctets[octetIndex] != localOctets[octetIndex])
                        {
                            possibleMatches.Remove(localAddress);
                        }

                        if (possibleMatches.Count == 1)
                        {
                            return (IPAddress)possibleMatches[0];
                        }
                    }
                }

                return null;
            }
            catch
            {
                //logger.Error("Exception GetDefaultIPAddress. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Calls the operating system command 'route print' to obtain the IP
        /// routing information.
        /// </summary>
        /// <returns>A string holding the output of the command.</returns>
        public static string CallRoute()
        {
            try
            {
                if (Platform == PlatformEnum.Windows)
                {
                    return CallShellCommand("route", "print");
                }
                else
                {
                    return CallShellCommand("route", "");
                }
            }
            catch (Exception excp)
            {
                //logger.Error("Exception call to 'route print': " + excp.Message);
                throw new ApplicationException("An attempt to call 'route print' failed. " + excp.Message);
            }
        }

        /// Creates a new process to execute a specified shell command and returns the output
        /// to the caller as a string.
        /// </summary>
        public static string CallShellCommand(string command, string commandLine)
        {
            Process osProcess = new Process();
            osProcess.StartInfo.CreateNoWindow = true;
            osProcess.StartInfo.UseShellExecute = false;
            //osProcess.StartInfo.UseShellExecute = true;
            osProcess.StartInfo.RedirectStandardOutput = true;

            osProcess.StartInfo.FileName = command;
            osProcess.StartInfo.Arguments = commandLine;

            osProcess.Start();

            osProcess.WaitForExit();
            return osProcess.StandardOutput.ReadToEnd();
        }

		#region Unit tests.

		#if UNITTEST

		[TestFixture]
		public class OSServicesUnitTest
		{		
			[TestFixtureSetUp]
			public void Init()
			{			
				// Redirect the logger to the console for unit testing.
				//Log.ConfigureUnitTestLogging();
				//logger.Info("BackupFileUnitTest: Test Logger.");
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			/// <summary>
			/// Test calling the operating system "route print" command.
			/// </summary>
			[Test]
			public void TestCallRoute()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsNotNull(CallRoute(), "The 'route print' command did not return anything.");
			}
		}

		#endif

		#endregion
	}
}
