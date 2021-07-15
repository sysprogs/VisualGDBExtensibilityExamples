using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using VisualGDBExtensibility;

namespace TelnetDemo
{
    class VirtualFile : QueuedUploadedFile
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
            TelnetTarget.TelnetTarget.Register();

            //TODO: change the parameters below to match your test configuration
            var target = new TelnetTarget.TelnetTarget(new TelnetTarget.TelnetParameters
            {
                Host = "192.168.0.153",
                UserName = "testuser",
                Password = "test"
            });

            //This should create a file called '/tmp/file.txt' on the target.
            target.SendFiles("/tmp", new[] { new VirtualFile("file.txt", "Hello, world\n") });

            ManualResetEvent done = new ManualResetEvent(false);

            int? code = null;
            //Running 'cat /tmp/file.txt' should output the contents of the file uploaded in the previous step.
            var cmd = target.CreateRemoteCommand("cat", "\"/tmp/file.txt\"", "", null, VisualGDBExtensibility.CommandFlags.None);
            cmd.CommandExited += (c, r) => { code = r; done.Set(); };
            cmd.TextReceived += (c, t, type) => Console.Write(t);
            cmd.Start();
            done.WaitOne();
            if (code.HasValue)
                Console.WriteLine("Command exited with code " + code.Value);
        }
    }
}
