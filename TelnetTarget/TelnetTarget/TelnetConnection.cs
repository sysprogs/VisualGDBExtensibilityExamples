using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace TelnetTarget
{
    /// <summary>
    /// This class represents one authenticated telnet connection. Note that it's very basic, unoptimized and does not handle a
    /// </summary>
    class TelnetConnection : IDisposable
    {
        TcpClient _Client;


        void HandleTelnetCommandSequence(byte[] bytes)
        {
            if (bytes.Length < 3)
                throw new Exception("Unsupported telnet command");

            SpecialTelnetCommand cmd = (SpecialTelnetCommand)bytes[1];
            if (cmd == SpecialTelnetCommand.DO && bytes[2] == 31)
            {
                //Terminal size negotiation. Report the maximum window width/height to avoid line wrapping
                SendBytes(0xFF, (byte)SpecialTelnetCommand.WILL, bytes[2]);
                SendBytes(0xFF, (byte)SpecialTelnetCommand.SB, bytes[2], 0xFE, 0xFE, 0xFE, 0xFE, (byte)SpecialTelnetCommand.IAC, (byte)SpecialTelnetCommand.SE);
                return;
            }

            switch (cmd)
            {
                case SpecialTelnetCommand.WILL:
                case SpecialTelnetCommand.WONT:
                    SendBytes(0xFF, (byte)SpecialTelnetCommand.DONT, bytes[2]);
                    break;
                case SpecialTelnetCommand.DO:
                case SpecialTelnetCommand.DONT:
                    SendBytes(0xFF, (byte)SpecialTelnetCommand.WONT, bytes[2]);
                    break;
            }
        }

        void SendBytes(params byte[] bytes)
        {
            _Client.GetStream().Write(bytes, 0, bytes.Length);
        }

        enum SpecialTelnetCommand : byte
        {
            IAC = 255,  //Interpret as command
            SB = 250, //Subnegotiation begin
            SE = 240, //Subnegotiation end
            WILL = 251,
            WONT = 252,
            DO = 253,
            DONT = 254,
        }

        byte[] _ReadBuffer = new byte[1024];
        int _ReadBufferSize, _ReadBufferPos;

        byte ReadByteOrThrow()
        {
            if (_ReadBufferPos >= _ReadBufferSize)
            {
                _ReadBufferPos = 0;
                _ReadBufferSize = _Client.GetStream().Read(_ReadBuffer, 0, _ReadBuffer.Length);

                if (_ReadBufferSize == 0)
                    throw new Exception("Telnet socket was closed");
            }

            return _ReadBuffer[_ReadBufferPos++];
        }

        public string ReadTextUntilEventAndHandleTelnetCommands(Func<string, bool> endCondition)
        {
            StringBuilder result = new StringBuilder();
            var stopWatch = Stopwatch.StartNew();
            bool noTimeout() => _Client.ReceiveTimeout > 0 ? stopWatch.ElapsedMilliseconds <= _Client.ReceiveTimeout : true;
            while(noTimeout())
            {
                byte ch = ReadByteOrThrow();
                if (ch == (byte)SpecialTelnetCommand.IAC)
                {
                    byte ch2 = ReadByteOrThrow();
                    if (ch2 == (byte)SpecialTelnetCommand.IAC)
                        result.Append((char)ch);
                    else if (ch2 == (byte)SpecialTelnetCommand.SB)
                    {
                        List<byte> data = new List<byte> { ch, ch2 };
                        byte ch3;
                        do
                        {
                            ch3 = ReadByteOrThrow();
                            data.Add(ch3);
                        } while (ch3 != (byte)SpecialTelnetCommand.SE);

                        HandleTelnetCommandSequence(data.ToArray());
                        continue;
                    }
                    else
                    {
                        byte ch3 = ReadByteOrThrow();
                        HandleTelnetCommandSequence(new[] { ch, ch2, ch3 });
                        continue;
                    }
                }
                else
                    result.Append((char)ch);

                //Debug.Write((char)ch);

                if (endCondition(result.ToString()))
                    break;
            }
            stopWatch.Stop();
            if(!noTimeout())
                throw new SocketException((int)SocketError.TimedOut);
            return result.ToString();
        }

        public void WriteText(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            if (Array.IndexOf(data, 0xFF) != -1)
            {
                data = data.SelectMany(b =>
                {
                    if (b == 0xFF)
                        return new byte[2] { 0xFF, 0xFF };
                    else
                        return new[] { b };
                }).ToArray();
            }
            _Client.GetStream().Write(data, 0, data.Length);
        }

        public TelnetConnection(TelnetParameters parameters)
        {
            _Client = new TcpClient();
            _Client.ReceiveTimeout = parameters.Timeout;
            _Client.SendTimeout = parameters.Timeout;

            var result = _Client.BeginConnect(parameters.Host, parameters.Port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(parameters.Timeout, true);
            if(success) {
                _Client.EndConnect(result);
            } else {
                _Client.Close();
                throw new SocketException((int)SocketError.TimedOut);
            }

            ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith("login:", StringComparison.InvariantCultureIgnoreCase));
            WriteText(parameters.UserName + "\r\n");
            ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith("password:", StringComparison.InvariantCultureIgnoreCase));
            WriteText(parameters.Password + "\r\n");

            string marker = "----com.sysprogs.telnet.start----";
            WriteText($"echo {marker}\n");
            string text = ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith(marker) || s.EndsWith("Login incorrect"));
            if (text.Contains("echo " + marker))
            {
                //This is the command echo. Wait for the actual command output.
                text = ReadTextUntilEventAndHandleTelnetCommands(s => s.EndsWith(marker) || s.EndsWith("Login incorrect"));
            }
            if (!text.Contains(marker))
                throw new Exception("Failed to login");
        }

        public void SetReceiveTimeout(int timeout)
        {
            _Client.ReceiveTimeout = timeout;
        }

        public void Dispose()
        {
            try
            {
                _Client.Close();
            }
            catch { }
        }
    }
}
