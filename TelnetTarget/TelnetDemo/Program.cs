using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using VisualGDBExtensibility;

namespace TelnetDemo
{
    class VirtualFile : TransferredFile
    {
        byte[] _Contents;

        public VirtualFile(string fileName, string contents) 
            : base(fileName, fileName)
        {
            _Contents = Encoding.UTF8.GetBytes(contents);
        }

        public override long Size => _Contents.Length;

        public override string UserFriendlyName => "Virtual file";

        public override void CopyTo(Stream stream, byte[] tempBuffer, long length, SimpleProgressHandler progress = null)
        {
            stream.Write(_Contents, 0, _Contents.Length);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var target = new TelnetTarget.TelnetTarget(new TelnetTarget.TelnetParameters
            {
                Host = "192.168.0.153",
                UserName = "testuser",
                Password = "test"
            });

            ManualResetEvent done = new ManualResetEvent(false);

            int? code = null;

            var cmd = target.CreateRemoteCommand("stat", "--printf %U \"/tmp\"", "", null, VisualGDBExtensibility.CommandFlags.None);
            cmd.CommandExited += (c, r) => { code = r; done.Set(); };
            cmd.TextReceived += (c, t, type) => Console.Write(t);
            cmd.Start();
            done.WaitOne();
            if (code.HasValue)
                Console.WriteLine("Command exited with code " + code.Value);

            target.SendFiles("/tmp", new[] { new VirtualFile("file.txt", "Hello, world") });
        }
    }
}
