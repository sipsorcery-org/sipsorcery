using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using log4net;
using SIPSorcery.Sys;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting;

namespace SIPSorcery.SIP.App
{

    /// <summary>
    /// Loads a script file, compiles it and watches it for any changes.
    /// </summary>
    public class ScriptLoader
    {
        private enum ScriptTypesEnum
        {
            None = 0,
            Python = 1,
            Ruby = 2,
        }

        private const string PYTHON_SCRIPT_EXTENSION = ".py";
        private const string RUBY_SCRIPT_EXTENSION = ".rb";

        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate m_monitorLogger = (e) => { };

        private string m_scriptPath;
        private string m_scriptText;
        private DateTime m_lastScriptChange;

        public event FileSystemEventHandler ScriptFileChanged;

        public ScriptLoader(
            SIPMonitorLogDelegate monitorLogger,
            string scriptPath)
        {

            try
            {
                m_monitorLogger = monitorLogger ?? m_monitorLogger;
                m_scriptPath = scriptPath;

                // File system watcher needs a fully qualified path.
                if (!m_scriptPath.Contains(Path.DirectorySeparatorChar.ToString()))
                {
                    m_scriptPath = Environment.CurrentDirectory + Path.DirectorySeparatorChar + m_scriptPath;
                }

                if (!File.Exists(m_scriptPath))
                {
                    throw new ApplicationException("Cannot load script file was not found at " + m_scriptPath + ".");
                }

                FileSystemWatcher runtimeWatcher = new FileSystemWatcher(Path.GetDirectoryName(m_scriptPath), Path.GetFileName(m_scriptPath));
                runtimeWatcher.Changed += new FileSystemEventHandler(ScriptChanged);
                runtimeWatcher.EnableRaisingEvents = true;
            }
            catch (Exception excp)
            {
                logger.Error("Exception ScriptLoader (ctor). " + excp.Message);
                throw excp;
            }
        }

        public CompiledCode GetCompiledScript()
        {
            try
            {
                m_scriptText = GetText();

                if (m_scriptText.IsNullOrBlank())
                {
                    throw new ApplicationException("Cannot load script, file was empty " + m_scriptPath + ".");
                }

                // Configure script engine.
                ScriptTypesEnum scriptType = GetScriptType(m_scriptPath);

                if (scriptType == ScriptTypesEnum.Python)
                {
                    logger.Debug("Compiling IronPython script file from " + m_scriptPath + ".");
                    ScriptRuntime scriptRuntime = IronPython.Hosting.Python.CreateRuntime();
                    ScriptScope scriptScope = scriptRuntime.CreateScope("IronPython");
                    CompiledCode compiledCode = scriptScope.Engine.CreateScriptSourceFromFile(m_scriptPath).Compile();
                    logger.Debug("IronPython compilation complete.");
                    return compiledCode;
                    //return scriptScope.Engine.CreateScriptSourceFromString(m_scriptText).Compile();
                }
                else if (scriptType == ScriptTypesEnum.Ruby)
                {
                    logger.Debug("Compiling IronRuby script file from " + m_scriptPath + ".");
                    ScriptRuntime scriptRuntime = IronRuby.Ruby.CreateRuntime();
                    ScriptScope scriptScope = scriptRuntime.CreateScope("IronRuby");
                    return scriptScope.Engine.CreateScriptSourceFromString(m_scriptText).Compile();
                }
                else
                {
                    throw new ApplicationException("ScriptLoader could not compile script, unrecognised proxy script type " + scriptType + ".");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCompiledScript. " + excp.Message);
                throw;
            }
        }

        public string GetText()
        {
            try
            {
                /*string tempPath = Path.GetDirectoryName(m_scriptPath) + Path.DirectorySeparatorChar + Path.GetFileName(m_scriptPath) + ".tmp";
                File.Copy(m_scriptPath, tempPath, true);

                using (StreamReader sr = new StreamReader(tempPath))
                {
                    m_scriptText = sr.ReadToEnd();
                    sr.Close();
                }

                return m_scriptText;*/

                return File.ReadAllText(m_scriptPath);
            }
            catch (IOException excp)
            {
                logger.Warn("IOException GetText (wait for 0.5s and try again). " + excp.Message);
                Thread.Sleep(500);
                return File.ReadAllText(m_scriptPath);
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetText. " + excp.Message);
                throw;
            }
        }

        private void ScriptChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (DateTime.Now.Subtract(m_lastScriptChange).TotalSeconds > 1)
                {
                    m_lastScriptChange = DateTime.Now;  // Prevent double re-loads. The file changed event fires twice when a file is saved.
                    logger.Debug("Script file changed " + m_scriptPath + ".");

                    if (ScriptFileChanged != null)
                    {
                        ScriptFileChanged(sender, e);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ScriptChanged. " + excp.Message);
            }
        }

        private ScriptTypesEnum GetScriptType(string scriptFileName)
        {
            string extension = Path.GetExtension(scriptFileName);

            switch (extension)
            {
                case PYTHON_SCRIPT_EXTENSION:
                    return ScriptTypesEnum.Python;
                case RUBY_SCRIPT_EXTENSION:
                    return ScriptTypesEnum.Ruby;
                default:
                    throw new ApplicationException("The script engine could not be identified for the script with extension of " + extension + ".");
            }
        }
    }
}
