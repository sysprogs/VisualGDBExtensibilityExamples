using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using VisualGDBExtensibility;

namespace TelnetTarget
{
    public class TelnetTarget : ICustomRemoteTargetEx, ILegacyBulkTransferTarget
    {
        private TelnetParameters _Parameters;

        Stack<TelnetConnection> _ConnectionPool = new Stack<TelnetConnection>();

        public TelnetTarget(TelnetParameters telnetParams)
        {
            _Parameters = telnetParams;
            _ConnectionPool.Push(new TelnetConnection(_Parameters));
        }

        public ExpectedEnvironmentStyle ExpectedEnvironmentStyle => ExpectedEnvironmentStyle.UnixStyleVars;

        public string TemporaryPath => "/tmp";

        TelnetConnection ProvideConnection()
        {
            lock(_ConnectionPool)
            {
                if (_ConnectionPool.Count > 0)
                    return _ConnectionPool.Pop();
            }
            return new TelnetConnection(_Parameters);
        }

        void ReturnConnectionToPool(TelnetConnection conn)
        {
            lock (_ConnectionPool)
                _ConnectionPool.Push(conn);
        }

        const bool UseExportCommandToSetEnvironment = true;

        class TelnetCommand : IRemoteCommand
        {
            private string _CommandLine;
            private TelnetConnection _Connection;
            private TelnetTarget _Target;

            string _BeginMarker, _EndMarker;
            private Thread _ReadThread;

            bool _ExitedNormally;

            public TelnetCommand(TelnetTarget telnetTarget, TelnetConnection telnetConnection, string commandLine)
            {
                _Target = telnetTarget;
                _Connection = telnetConnection;
                _CommandLine = commandLine;

                //We will use the markers below to determine where the output of our command starts and ends
                _BeginMarker = $"---com.sysprogs.telnet.begin.{Guid.NewGuid()}---";
                _EndMarker = $"c@m.sysprogs.telnet.end.{Guid.NewGuid()}---";
                _ReadThread = new Thread(ReadThreadBody);
            }

            void ReadThreadBody()
            {
                try
                {
                    Thread.CurrentThread.Name = "Telnet command readout thread";

                    if (_CommandLine == null)
                    {
                        //This is a special console that already had an active command when this class received it. Hence we don't wait for the start marker.
                    }
                    else
                    {
                        //Read and discard everything before the start marker. These lines to not belong to our command.
                        _Connection.SetTimeout(1000);
                        string text = _Connection.ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith(_BeginMarker + "\r\n"));
                        _Connection.SetTimeout(0);
                    }

                    //We don't know for sure where the command output ends and our end marker starts. Hence we have to guess it and immediately output everything that does not
                    //look like the end marker. This has a bad side effect that all output looking like the beginning of our end marker will be delayed until we are 100% sure that
                    //it's not the actual marker;

                    bool atEndOfLine = true;

                    for (;;)
                    {
                        string output = _Connection.ReadTextUntilEventAndHandleTelnetCommands(s => !atEndOfLine || (!_EndMarker.StartsWith(s) || s == _EndMarker));
                        if (output == _EndMarker)
                        {
                            string code = _Connection.ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith("E"));
                            int parsedCode;
                            if (code.EndsWith("E") && int.TryParse(code.TrimEnd('E'), out parsedCode))
                                CommandExited?.Invoke(this, parsedCode);
                            else
                                CommandExited?.Invoke(this, null);

                            _ExitedNormally = true;
                            return;
                        }

                        atEndOfLine = output.EndsWith("\n");
                        TextReceived?.Invoke(this, output, CommandStreamType.Stdout);
                    }

                }
                catch
                {
                    CommandExited?.Invoke(this, null);
                }
            }

            public bool CanSendCtrlC => true;

            public event CommandExitedHandler CommandExited;
            public event CommandTextReceived TextReceived;

            public void Dispose()
            {
                if (_ExitedNormally && _Connection != null && _Target != null)
                {
                    _Target.ReturnConnectionToPool(_Connection);
                    _Target = null;
                    _Connection = null;
                }
            }

            public void FlushPendingOutputEvents()
            {
            }

            public bool SendCtrlC()
            {
                _Connection.WriteText("\x03");
                return true;
            }

            public void SendInput(string text)
            {
                _Connection.WriteText(text);
            }

            public void Start()
            {
                if (_CommandLine != null)
                    _Connection.WriteText($"echo {_BeginMarker} ; {_CommandLine} ; echo ; echo {_EndMarker}$?E\r\n");
                _ReadThread.Start();
            }
        }

        class TelnetConsole : TelnetCommand, IRemoteConsole
        {
            public TelnetConsole(TelnetTarget telnetTarget, TelnetConnection telnetConnection, string path) 
                : base(telnetTarget, telnetConnection, null)
            {
                Path = path;
            }

            public string Path { get; private set; }
        }

        public IRemoteCommand CreateRemoteCommand(string command, string args, string directory, EnvironmentVariableRecord[] environment, CommandFlags flags)
        {
            string commandLine;
            if (command.StartsWith("${") && command.EndsWith("}") && !command.Contains(" "))
            {
                //Yocto environment files can define "CC" to be something like "arm-linux-gcc -mcpu=xxx". Enclosing ${CC} with quotes would break it as bash would try to treat the entire command line as the command name.
                commandLine = command;
            }
            else if (!string.IsNullOrEmpty(args) || !string.IsNullOrEmpty(directory))
                commandLine = $"\"{command}\"";
            else
                commandLine = command;

            if (!string.IsNullOrEmpty(args))
                commandLine += " " + args;

            StringBuilder envPrefix = new StringBuilder();
            if (environment != null)
                foreach (var rec in environment)
                {
                    if (UseExportCommandToSetEnvironment)
                        envPrefix.AppendFormat("export {0}=\"{1}\" && ", rec.Key, rec.Value.Replace("\"", "\\\""));
                    else
                        envPrefix.AppendFormat("setenv {0} \"{1}\" && ", rec.Key, rec.Value.Replace("\"", "\\\""));
                }

            if (envPrefix.Length > 0)
                commandLine = envPrefix + commandLine;

            if ((flags & CommandFlags.DisableCharacterEchoing) != CommandFlags.None)
                commandLine = "stty echo && " + commandLine;
            else
                commandLine = "stty -echo && " + commandLine;

            if (!string.IsNullOrEmpty(directory))
                commandLine = string.Format("cd \"{0}\" && {1}", directory, commandLine);

            return new TelnetCommand(this, ProvideConnection(), commandLine);
        }

        public IRemoteConsole CreateVirtualConsole()
        {
            var conn = ProvideConnection();
            string marker = $"com.sysprogs.tty:{Guid.NewGuid().ToString()}:";
            conn.WriteText($"echo {marker} ; tty ; sleep {int.MaxValue}\r\n");

            conn.SetTimeout(2000);
            string prefix = conn.ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith("\r\n" + marker + "\r\n"));
            string tty = conn.ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith("\r\n")).TrimEnd();
            conn.SetTimeout(0);

            return new TelnetConsole(this, conn, tty);
        }

        public void Dispose()
        {
            TelnetConnection[] connections;
            lock (_ConnectionPool)
            {
                connections = _ConnectionPool.ToArray();
                _ConnectionPool.Clear();
            }

            foreach (var conn in connections)
                conn.Dispose();
        }

        public byte[] ReadFileHeader(string fileName, int headerSize)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// This method is called by VisualGDB when the user steps into a source file that is not available on the Windows machine.
        /// It should download the source file to the provided temporary location so that VisualGDB can display it.
        /// Although this example does not provide an implementation for this method, you can easily implement it by running the 'base64' command and reading its output.
        /// </summary>
        public void RetrieveFile(string targetPath, string localFileOnDisk, ILongOperationCallbacks callbacks = null)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Telnet connections don't provide a reliable way of sending and receiving files, so we have to work around this by encoding each file with base64 and sending it separately.
        /// </summary>
        public void SendFiles(string baseTargetPath, IEnumerable<QueuedUploadedFile> files, ILongOperationCallbacks callbacks = null)
        {
            long totalSize = 0, bytesDone = 0;
            int totalCount = 0, filesDone = 0;
            foreach (var file in files)
            {
                totalSize += file.Size;
                totalCount++;
            }

            HashSet<string> createdPaths = new HashSet<string>();

            byte[] tempBuffer = new byte[65536];

            ManualResetEvent done = new ManualResetEvent(false);

            foreach (var file in files)
            {
                string fullPath = file.RemotePath;
                if (!fullPath.StartsWith("/"))
                    fullPath = baseTargetPath + "/" + fullPath;

                int idx = fullPath.LastIndexOf('/');
                string dir = fullPath.Substring(0, idx);
                if (!createdPaths.Contains(dir))
                {
                    using (var cmd = CreateRemoteCommand($"mkdir -p \"{dir}\"", "", "", null, CommandFlags.None))
                    {
                        done.Reset();
                        cmd.CommandExited += (s, code) => done.Set();
                        cmd.Start();
                        done.WaitOne();
                    }
                }

                int exitCode = -1;
                using (var cmd = CreateRemoteCommand($"base64 -d > \"{fullPath}\"", "", "", null, CommandFlags.None))
                {
                    cmd.CommandExited += (s, code) => { done.Set(); exitCode = code ?? -1; };
                    done.Reset();
                    cmd.Start();

                    using (var stream = new MemoryStream())
                    {
                        file.CopyTo(stream, tempBuffer, file.Size);
                        cmd.SendInput(Convert.ToBase64String(stream.ToArray(), Base64FormattingOptions.InsertLineBreaks));
                    }

                    cmd.SendInput("\r\n\x04");
                    done.WaitOne();
                }

                if (exitCode == 0)
                {
                    filesDone++;
                    bytesDone += file.Size;
                    callbacks?.ReportMultiFileProgress(file.UserFriendlyName, filesDone, totalCount, bytesDone, totalSize);
                }
                else
                {
                    callbacks?.ReportFileError(fullPath, new Exception("'base64' exited with code " + exitCode));
                }
            }

        }

        public void CreateAndRetrieveTarball(string targetDirectoryToPack, string localFileOnDisk, string tarMode, ILongOperationCallbacks callbacks)
        {
            var cmd = CreateRemoteCommand($"cd \"{targetDirectoryToPack}\" && tar {tarMode} - * | base64", "", "", null, CommandFlags.None);
            ManualResetEvent done = new ManualResetEvent(false);
            StringBuilder builtOutput = new StringBuilder();
            var rgBase64 = new System.Text.RegularExpressions.Regex("^[A-Za-z0-9+/=\r\n]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

            using (var fs = File.Create(localFileOnDisk))
            {
                cmd.CommandExited += (s, st) => done.Set();
                cmd.TextReceived += (s, text, type) =>
                {
                    builtOutput.Append(text);
                    if (text.IndexOf('\n') != -1)
                    {
                        var str = builtOutput.ToString();
                        int lineStart = 0, lineEnd;
                        for (lineStart = 0; ;lineStart = lineEnd + 1)
                        {
                            lineEnd = str.IndexOf('\n', lineStart);
                            if (lineEnd == -1)
                                break;

                            string line = str.Substring(lineStart, lineEnd - lineStart + 1);
                            if (rgBase64.IsMatch(line))
                            {
                                var dataBlock = Convert.FromBase64String(line);
                                fs.Write(dataBlock, 0, dataBlock.Length);
                            }
                            else
                                callbacks?.ReportOutputLine(line);
                        }

                        if (lineStart >= builtOutput.Length)
                            builtOutput.Clear();
                        else
                            builtOutput.Remove(0, lineStart + 1);
                    }
                };

                cmd.Start();

                done.WaitOne();
                cmd.FlushPendingOutputEvents();

                var data = Convert.FromBase64String(builtOutput.ToString());
                fs.Write(data, 0, data.Length);
            }
        }
    }
}
