//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Main entry point to start the application. Includes logic to self install and uninstall as a Windows Service.
//
// History:
// 04 Mar 2016	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2016 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Configuration.Install;
using System.Threading.Tasks;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net.WebRtc
{
    class Program
    {
        private static ILog logger = AppState.logger;

        static void Main(string[] args)
        {
            try
            {
                //Windows service has system32 as default working folder, we change the working dir to install dir for file access
                System.IO.Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
                logger.Debug("Setting current directory to " + System.AppDomain.CurrentDomain.BaseDirectory);

                var daemon = new WebRTCDaemon();

                if (args != null && args.Length == 1 && args[0] == "-i")
                {
                    try
                    {
                        using (AssemblyInstaller inst = new AssemblyInstaller(typeof(Program).Assembly, args))
                        {
                            IDictionary state = new Hashtable();
                            inst.UseNewContext = true;
                            try
                            {
                                inst.Install(state);
                                inst.Commit(state);
                            }
                            catch
                            {
                                try
                                {
                                    inst.Rollback(state);
                                }
                                catch { }
                                throw;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                    }
                }
                else if (args != null && args.Length == 1 && args[0] == "-u")
                {
                    try
                    {
                        using (AssemblyInstaller inst = new AssemblyInstaller(typeof(Program).Assembly, args))
                        {
                            IDictionary state = new Hashtable();
                            inst.UseNewContext = true;
                            try
                            {

                                inst.Uninstall(state);
                            }
                            catch
                            {
                                try
                                {
                                    inst.Rollback(state);
                                }
                                catch { }
                                throw;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                    }
                }
                else if ((args != null && args.Length == 1 && args[0].StartsWith("-c")) || System.Environment.UserInteractive == true)
                {
                    Task.Run(daemon.Start);

                    Console.WriteLine("Press q to quit at any time.");
                    while(true)
                    {
                        string option = Console.ReadLine();
                        if(option?.ToLower() == "q")
                        {
                            Console.WriteLine("User requested quit.");
                            break;
                        }
                    }

                    daemon.Stop();
                }
                else
                {
                    System.ServiceProcess.ServiceBase[] ServicesToRun;
                    ServicesToRun = new System.ServiceProcess.ServiceBase[] { new WebRTCService(daemon) };
                    System.ServiceProcess.ServiceBase.Run(ServicesToRun);
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp);
            }
        }
    }
}
