using System;
using System.Reflection;
using System.Runtime.InteropServices;
using RT.PropellerApi;
using RT.Servers;
using RT.Util;

[assembly: AssemblyTitle("FileServer")]
[assembly: AssemblyDescription("Provides a Propeller module that simply serves the files in a specified directory.")]
[assembly: AssemblyCompany("CuteBits")]
[assembly: AssemblyProduct("Propeller FileServer")]
[assembly: AssemblyCopyright("Copyright © CuteBits 2016")]
[assembly: ComVisible(false)]
[assembly: Guid("252158cb-adeb-41ad-84d1-e82969959096")]
[assembly: AssemblyVersion("1.0.9999.9999")]
[assembly: AssemblyFileVersion("1.0.9999.9999")]

namespace RT.Propeller.Modules
{
    public sealed class FileServer : PropellerModuleBase<FileServerSettings>
    {
        public override string Name { get { return "FileServer"; } }

        private FileSystemHandler Handler;

        public override void Init()
        {
            Handler = new FileSystemHandler(Settings.Directory, Settings.Options);
        }

        public override HttpResponse Handle(HttpRequest req)
        {
            return Handler.Handle(req);
        }
    }

    /// <summary>Contains the settings for the FileServer Propeller module.</summary>
    public sealed class FileServerSettings
    {
        /// <summary>The directory from which to serve files.</summary>
        public string Directory;

        /// <summary>All the wealth of options for serving files and directories.</summary>
        public FileSystemOptions Options;
    }
}
