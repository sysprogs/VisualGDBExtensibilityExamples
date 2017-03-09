using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using VisualGDBExtensibility;

namespace TelnetTarget
{
    public class TelnetParameters
    {
        public string Host { get; set; }
        public int Port { get; set; } = 23;
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    /// <summary>
    /// This is a very simple target factory example. It will remember the target parameters in the (hostname).xml files in %LOCALAPPDATA%\VisualGDB\TelnetConnections.
    /// Each .xml file will contain a serialized instance of TelnetParameters class defined above.
    /// </summary>
    public class TelnetTargetFactory : ICustomTargetFactory
    {
        string _TargetDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualGDB", "TelnetConnections");

        public string NewTargetHint => "Create a new telnet target";

        public string UniqueID => "com.sysprogs.targets.telnet";

        public CustomTargetConnectionParameters CreateAndRememberNewConnectionInteractively()
        {
            var wnd = new NewTargetWindow { DataContext = new TelnetParameters() };
            if (wnd.ShowDialog() == true)
            {
                var parameters = wnd.DataContext as TelnetParameters;
                if (string.IsNullOrEmpty(parameters?.Host))
                    throw new Exception("Host name not specified");

                Directory.CreateDirectory(_TargetDirectory);
                var ser = new XmlSerializer(typeof(TelnetParameters));
                using (var fs = File.Create(Path.Combine(_TargetDirectory, parameters.Host + ".xml")))
                    ser.Serialize(fs, parameters);

                return new CustomTargetConnectionParameters(parameters.Host);
            }
            throw new OperationCanceledException();
        }

        public ICustomRemoteTarget CreateTarget(CustomTargetConnectionParameters parameters)
        {
            var fn = Path.Combine(_TargetDirectory, parameters.TargetSpecifier + ".xml");
            if (!File.Exists(fn))
                throw new FileNotFoundException("Missing " + fn);

            TelnetParameters telnetParams;
            var ser = new XmlSerializer(typeof(TelnetParameters));
            using (var fs = File.OpenRead(fn))
                telnetParams = (TelnetParameters)ser.Deserialize(fs);

            return new TelnetTarget(telnetParams);
        }

        public CustomTargetConnectionParameters[] EnumerateKnownConnections()
        {
            if (!Directory.Exists(_TargetDirectory))
                return null;

            return Directory.GetFiles(_TargetDirectory, "*.xml")
                .Select(fn => Path.GetFileNameWithoutExtension(fn))
                .Select(name => new CustomTargetConnectionParameters(name))
                .ToArray();
        }

        public string GetUserFriendlyConnectionName(CustomTargetConnectionParameters parameters) => parameters.TargetSpecifier;
    }
}
