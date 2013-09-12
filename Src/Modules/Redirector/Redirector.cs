using System;
using System.Reflection;
using System.Runtime.InteropServices;
using RT.PropellerApi;
using RT.Servers;
using RT.Util.Serialization;

[assembly: AssemblyTitle("Redirector")]
[assembly: AssemblyDescription("Provides a Propeller module that simply redirects to another URL.")]
[assembly: AssemblyCompany("CuteBits")]
[assembly: AssemblyProduct("Propeller Redirector")]
[assembly: AssemblyCopyright("Copyright © CuteBits 2013")]
[assembly: ComVisible(false)]
[assembly: Guid("1a75d829-1b88-42ff-b1f3-c8e925c04d7b")]
[assembly: AssemblyVersion("1.0.9999.9999")]
[assembly: AssemblyFileVersion("1.0.9999.9999")]

namespace RT.Propeller.Modules
{
    public sealed class Redirector : PropellerModuleBase<RedirectorSettings>
    {
        public override string Name { get { return "Redirector"; } }

        public override HttpResponse Handle(HttpRequest req)
        {
            return HttpResponse.Redirect(Settings.RedirectToUrl);
        }
    }

    public sealed class RedirectorSettings
    {
        [ClassifyNotNull]
        public string RedirectToUrl = "http://example.com/";
    }
}
