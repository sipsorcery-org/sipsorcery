// ============================================================================
// FileName: RateBulkUpdater.cs
//
// Description:
// A daemon to allow the bulk update of rates for the real-time call control system.
//
// Author(s):
// Aaron Clauson
//
// History:
// 19 Aug 2013	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2013 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery Pty Ltd 
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Transactions;
using SIPSorcery.Entities;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    /// <summary>
    /// Format of bulk rate update files:
    ///  DELETE_ALL - Deletes all rates, in preparation for a clean insert of all rates, can only be the first line of the file.
    ///  I,[Description],[Prefix],[Rate],[SetupCost],[IncrementSeconds],[RateCode],[RatePlan] - Inserts a new rate.
    ///  U,[ID],[Description],[Prefix],[Rate],[SetupCost],[IncrementSeconds],[RateCode],[RatePlan] - Updates an existing rate with ID [ID].
    ///  D,[ID] - Deletes a rate with ID [ID].
    /// </summary>
    public class RateBulkUpdater
    {
        private const string RATE_BULK_THREAD_NAME = "rate-bulk";
        private const int CHECK_FOR_FILE_INTERVAL = 30000;
        private const string EMAIL_FROM_ADDRESS = "admin@sipsorcery.com";

        private const string DELETE_ALL_RATES_KEY = "DELETE_ALL";
        private const string INSERT_COMMAND_KEY = "I";
        private const string UPDATE_COMMAND_KEY = "U";
        private const string DELETE_COMMAND_KEY = "D";

        private static ILog logger = AppState.logger;

        private static readonly string _monitorDirectory = AppState.GetConfigSetting("RateBulkUpdaterDirectory");
        private static readonly string _badUpdateFilesDirectory = AppState.GetConfigSetting("RateBulkUpdaterBadFilesDirectory");
        private static readonly string _processedUpdateFilesDirectory = AppState.GetConfigSetting("RateBulkUpdaterProcessedFilesDirectory");

        private SIPMonitorLogDelegate Log_External;
        private CustomerDataLayer m_customerDataLayer = new CustomerDataLayer();
        private CustomerAccountDataLayer m_customerAccountDataLayer = new CustomerAccountDataLayer();
        private RateDataLayer m_rateDataLayer = new RateDataLayer();
        private Dictionary<string, long> m_newFiles = new Dictionary<string, long>();   // Keeps track of new files to determine if there have been any changes within a set period.

        private bool m_exit;

        public RateBulkUpdater(SIPMonitorLogDelegate logDelegate)
        {
            Log_External = logDelegate;
        }

        public void Start()
        {
            try
            {
                if (Directory.Exists(_monitorDirectory))
                {
                    logger.Debug("Bulk rate updater commencing montioring on directory " + _monitorDirectory + ".");

                    ThreadPool.QueueUserWorkItem(delegate { MonitorBulkRatesDirectory(); });
                }
                else
                {
                    logger.Warn("The bulk rate updater directory " + _monitorDirectory + " does not exist.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception BulRateUpdater.Start. " + excp);
            }
        }

        private void MonitorBulkRatesDirectory()
        {
            try
            {
                Thread.CurrentThread.Name = "bulkrate";

                if (!Directory.Exists(_monitorDirectory))
                {
                    logger.Error("The bulk rates directory does not exist " + _monitorDirectory + ".");
                }
                else
                {
                    logger.Debug("Starting monitor of bulk rates directory " + _monitorDirectory + ".");

                    while (!m_exit)
                    {
                        var newFile = Directory.GetFiles(_monitorDirectory).FirstOrDefault();

                        while (newFile != null)
                        {
                            FileInfo newFileInfo = new FileInfo(newFile);

                            if (!m_newFiles.ContainsKey(newFile))
                            {
                                logger.Warn("First appearance of " + newFile + " recording length " + newFileInfo.Length + ".");
                                m_newFiles.Add(newFile, newFileInfo.Length);
                            }
                            else
                            {
                                if (m_newFiles[newFile] >= newFileInfo.Length)
                                {
                                    m_newFiles.Remove(newFile);
                                    ProcessBulkRateFile(newFile);
                                }
                                else
                                {
                                    logger.Warn("The length of " + newFile + " is larger than the last check, previously " + m_newFiles[newFile] + " now " + newFileInfo.Length + ".");
                                    m_newFiles[newFile] = newFileInfo.Length;
                                }
                            }

                            Thread.Sleep(CHECK_FOR_FILE_INTERVAL);

                            newFile = Directory.GetFiles(_monitorDirectory).FirstOrDefault();
                        }

                        Thread.Sleep(CHECK_FOR_FILE_INTERVAL);
                    }
                }

                logger.Debug("Monitor bulk rates directory thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorBulkRatesDirectory. " + excp);
            }
        }

        private void ProcessBulkRateFile(string fullPath)
        {
            string fileName = Path.GetFileName(fullPath);

            bool wasSuccess = true;
            string updateLog = "Commencing bulk rate update of file " + fileName + " at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".\r\n";
            string customerEmailAddress = null;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                logger.Debug("BulkRateFileCreated new file created " + fullPath + ".");

                // Find the customer that the new file belongs to.
                string ftpPrefix = fileName.Substring(0, fileName.IndexOf('_'));
                var customer = m_customerDataLayer.GetForFTPPrefix(ftpPrefix);

                if (customer == null)
                {
                    string badFileName = _badUpdateFilesDirectory + DateTime.Now.ToString("ddMMMyyyyHHmmss") + "_" + fileName;
                    logger.Warn("No customer record found with an FTP prefix of " + ftpPrefix + ", moving to bad file directory " + badFileName + ".");
                    File.Move(fullPath, badFileName);
                }
                else
                {
                    string owner = customer.Name;
                    customerEmailAddress = customer.EmailAddress;
                    logger.Debug("Processing bulk rate update file for " + owner + ".");

                    using (FileStream fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        StreamReader sr = new StreamReader(fs);

                        using (var transaction = new TransactionScope())
                        {
                            bool isFirstLine = true;

                            while (!sr.EndOfStream)
                            {
                                string rateUpdate = sr.ReadLine();
                                logger.Debug("Processing rate update line: " + rateUpdate);
                                updateLog += rateUpdate.Trim();

                                if (rateUpdate.NotNullOrBlank())
                                {
                                    if (rateUpdate.Trim() == DELETE_ALL_RATES_KEY && isFirstLine)
                                    {
                                        logger.Debug("Deleting all rates.");
                                        m_rateDataLayer.DeleteAll(owner);
                                        updateLog += " <- All rates successfully deleted.\r\n";
                                    }
                                    else
                                    {
                                        string[] rateUpdateFields = rateUpdate.Split(',');

                                        string command = rateUpdateFields[0].ToUpper();

                                        switch (command)
                                        {
                                            case INSERT_COMMAND_KEY:
                                                if (rateUpdateFields.Length < 8)
                                                {
                                                    wasSuccess = false;
                                                    updateLog += " <- Insert command failed, the required number of fields were not present.\r\n";
                                                    throw new ApplicationException("A rate insert command was not processed as not enough fields were present. " + rateUpdate);
                                                }
                                                else
                                                {
                                                    logger.Debug("Inserting new rate for " + rateUpdateFields[1] + ".");

                                                    var insertRate = new Rate()
                                                    {
                                                        Owner = owner,
                                                        Description = rateUpdateFields[1],
                                                        Prefix = rateUpdateFields[2],
                                                        Rate1 = Convert.ToDecimal(rateUpdateFields[3]),
                                                        SetupCost = Convert.ToDecimal(rateUpdateFields[4]),
                                                        IncrementSeconds = Convert.ToInt32(rateUpdateFields[5]),
                                                        RateCode = rateUpdateFields[6],
                                                        RatePlan = Convert.ToInt32(rateUpdateFields[7])
                                                    };

                                                    m_rateDataLayer.Add(insertRate);

                                                    updateLog += " <- Insert command successful.\r\n";
                                                }

                                                break;

                                            case UPDATE_COMMAND_KEY:
                                                if (rateUpdateFields.Length < 9)
                                                {
                                                    wasSuccess = false;
                                                    updateLog += " <- Update command failed, the required number of fields were not present.\r\n";
                                                    throw new ApplicationException("A rate update command was not processed as not enough fields were present. " + rateUpdate);
                                                }
                                                else
                                                {
                                                    string updateRateID = rateUpdateFields[1];
                                                    logger.Debug("Updating rate with ID " + updateRateID + ".");

                                                    var updateRate = m_rateDataLayer.Get(updateRateID, owner);

                                                    if (updateRate != null)
                                                    {
                                                        updateRate.Description = rateUpdateFields[2];
                                                        updateRate.Prefix = rateUpdateFields[3];
                                                        updateRate.Rate1 = Convert.ToDecimal(rateUpdateFields[4]);
                                                        updateRate.SetupCost = Convert.ToDecimal(rateUpdateFields[5]);
                                                        updateRate.IncrementSeconds = Convert.ToInt32(rateUpdateFields[6]);
                                                        updateRate.RateCode = rateUpdateFields[7];
                                                        updateRate.RatePlan = Convert.ToInt32(rateUpdateFields[8]);

                                                        m_rateDataLayer.Update(updateRate);

                                                        updateLog += " <- Update command successful.\r\n";
                                                    }
                                                    else
                                                    {
                                                        wasSuccess = false;
                                                        updateLog += " <- Update command failed, the rate to update could not be found.\r\n";
                                                        throw new ApplicationException("The rate to update could not be found.");
                                                    }
                                                }

                                                break;

                                            case DELETE_COMMAND_KEY:
                                                string deleteRateID = rateUpdateFields[1];
                                                logger.Debug("Deleting rate with ID " + deleteRateID + ".");

                                                var deleteRate = m_rateDataLayer.Get(deleteRateID, owner);

                                                if (deleteRate != null)
                                                {
                                                    m_rateDataLayer.Delete(deleteRate.ID);
                                                }
                                                else
                                                {
                                                    wasSuccess = false;
                                                    updateLog += " <- Delete command failed, the rate to delete could not be found.\r\n";
                                                    throw new ApplicationException("The rate to delete could not be found.");
                                                }
                                                break;

                                            default:
                                                wasSuccess = false;
                                                updateLog += " <- Command was not recognised.\r\n";
                                                throw new ApplicationException("Command " + command + " was not recognised, ignoring.");
                                        }
                                    }

                                    isFirstLine = false;
                                }
                            }

                            transaction.Complete();
                        }
                    }

                    sw.Stop();
                    updateLog += "Successfully completed bulk rate update of " + fileName + " at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " in " + sw.Elapsed.TotalSeconds.ToString("0") + "s.";
                    logger.Debug("Successfully processed bulk rate update file " + fileName + ", moving to processed directory.");
                    File.Move(fullPath, _processedUpdateFilesDirectory + DateTime.Now.ToString("ddMMMyyyyHHmmss") + "_" + fileName);
                }
            }
            catch (Exception excp)
            {
                wasSuccess = false;
                updateLog += " <- Exception " + excp.GetType().ToString() + " " + excp.Message + ".\r\n";
                logger.Error("Exception ProcessBulkRateFile. " + excp);

                try
                {
                    File.Move(fullPath, _badUpdateFilesDirectory + DateTime.Now.ToString("ddMMMyyyyHHmmss") + "_" + fileName);
                }
                catch (Exception moveExcp)
                {
                    logger.Error("Exception ProcessBulkRateFile moving bad file. " + moveExcp);
                }
            }
            finally
            {
                if (customerEmailAddress.NotNullOrBlank())
                {
                    try
                    {
                        logger.Debug("Sending bulk rate update result to " + customerEmailAddress + ".");
                        string subject = (wasSuccess) ? "SIP Sorcery Bulk Rate Update Success" : "SIP Sorcery Bulk Rate Update Failure";
                        SIPSorcerySMTP.SendEmail(customerEmailAddress, EMAIL_FROM_ADDRESS, null, EMAIL_FROM_ADDRESS, subject, updateLog);
                    }
                    catch (Exception sendResultExcp)
                    {
                        logger.Error("Exception ProcessBulkRateFile sending result email. " + sendResultExcp);
                    }
                }
            }
        }

        public void Stop()
        {
            logger.Debug("Bulk rate updater Stop.");
            m_exit = true;
        }
    }
}
