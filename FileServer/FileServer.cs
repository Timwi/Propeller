using RT.PropellerApi;
using RT.Servers;

namespace RT.Propeller.Modules
{
    public sealed class FileServer : PropellerModuleBase<FileServerSettings>
    {
        public override string Name => "FileServer";

        private FileSystemHandler Handler;

        public override void Init()
        {
            Handler = Settings?.Directory == null ? null : new FileSystemHandler(Settings.Directory, Settings.Options);
        }

        public override HttpResponse Handle(HttpRequest req) =>
            Handler == null ? HttpResponse.Html("<h1>Error</h1><p>The file server is not configured.</p>", HttpStatusCode._503_ServiceUnavailable) : Handler.Handle(req);
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
