using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using RT.PropellerApi;
using RT.Servers;
using RT.Util.ExtensionMethods;
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
            HttpUrl newUrl = new HttpUrl(
                Settings.Protocol == RedirectProtocol.Keep ? req.Url.Https : Settings.Protocol == RedirectProtocol.Https,
                Settings.Domain == null ? req.Url.WithDomainParent().Domain :
                (Settings.PrependSubdomain ? req.Url.Domain : "") + Settings.Domain,
                Settings.Path == null ? req.Url.WithPathParent().Path :
                Settings.Path + (Settings.AppendSubpath ? req.Url.Path : "")
            );

            switch (Settings.QueryBehavior)
            {
                case RedirectQueryParameters.Keep:
                    newUrl.Query = req.Url.Query;
                    break;

                case RedirectQueryParameters.Remove:
                    break;

                case RedirectQueryParameters.Replace:
                    newUrl.Query = Settings.QueryParameters;
                    break;

                case RedirectQueryParameters.Append:
                    newUrl.Query = req.Url.Query.Concat(Settings.QueryParameters);
                    break;

                case RedirectQueryParameters.Prepend:
                    newUrl.Query = Settings.QueryParameters.Concat(req.Url.Query);
                    break;

                case RedirectQueryParameters.AddMissingKeys:
                    var keysAlready = req.Url.Query.Select(kvp => kvp.Key).ToHashSet();
                    newUrl.Query = req.Url.Query.Concat(Settings.QueryParameters.Where(kvp => !keysAlready.Contains(kvp.Key)));
                    break;

                case RedirectQueryParameters.OverrideKeys:
                    var keysOverride = Settings.QueryParameters.Select(kvp => kvp.Key).ToHashSet();
                    newUrl.Query = req.Url.Query.Where(kvp => !keysOverride.Contains(kvp.Key)).Concat(Settings.QueryParameters);
                    break;

                case RedirectQueryParameters.IfEmpty:
                    newUrl.Query = req.Url.Query.Any() ? req.Url.Query : Settings.QueryParameters;
                    break;
            }

            return HttpResponse.Redirect(newUrl.ToFull());
        }
    }

    /// <summary>Specifies how Redirector should change the URL’s protocol.</summary>
    public enum RedirectProtocol
    {
        /// <summary>Keep HTTP or HTTPS depending on the incoming URL.</summary>
        Keep,
        /// <summary>Switch the protocol to HTTP.</summary>
        Http,
        /// <summary>Switch the protocol to HTTPS.</summary>
        Https
    }

    /// <summary>Specifies how Redirector should deal with the query parameters.</summary>
    public enum RedirectQueryParameters
    {
        /// <summary>
        ///     The incoming query parameters are left unchanged and <see cref="RedirectorSettings.QueryParameters"/> is
        ///     ignored.</summary>
        Keep,

        /// <summary>All query parameters are removed and <see cref="RedirectorSettings.QueryParameters"/> is ignored.</summary>
        Remove,

        /// <summary>
        ///     The <see cref="RedirectorSettings.QueryParameters"/> are used and the incoming query parameters are discarded.</summary>
        Replace,

        /// <summary>
        ///     The <see cref="RedirectorSettings.QueryParameters"/> are appended to the incoming query parameters. Duplicated
        ///     keys are kept.</summary>
        Append,

        /// <summary>
        ///     The <see cref="RedirectorSettings.QueryParameters"/> are prepended to the incoming query parameters.
        ///     Duplicated keys are kept.</summary>
        Prepend,

        /// <summary>
        ///     Only values from <see cref="RedirectorSettings.QueryParameters"/> are added whose key is not already part of
        ///     the incoming query parameters.</summary>
        AddMissingKeys,

        /// <summary>
        ///     All values from <see cref="RedirectorSettings.QueryParameters"/> are used. Incoming query parameters are
        ///     retained only if the key is not in <see cref="RedirectorSettings.QueryParameters"/>.</summary>
        OverrideKeys,

        /// <summary>
        ///     The incoming query parameters are used if there are any; <see cref="RedirectorSettings.QueryParameters"/> is
        ///     used only if the incoming query parameters are empty.</summary>
        IfEmpty
    }

    /// <summary>Contains the settings for the Redirector Propeller module.</summary>
    public sealed class RedirectorSettings : IClassifyObjectProcessor
    {
        /// <summary>
        ///     Set this string to a complete or incomplete URL to automatically configure the settings from it when the
        ///     settings are saved.</summary>
        /// <example>
        ///     <list type="table">
        ///         <item><term>
        ///             <c>"http://www.example.com/path"</c></term>
        ///         <description>
        ///             Sets <see cref="Protocol"/> to <see cref="RedirectProtocol.Http"/>, <see cref="Domain"/> to
        ///             <c>"www.example.com"</c>, <see cref="PrependSubdomain"/> to <c>false</c>, <see cref="Path"/> to
        ///             <c>"/path"</c>, <see cref="AppendSubpath"/> to <c>false</c> and <see cref="QueryBehavior"/> to <see
        ///             cref="RedirectQueryParameters.Remove"/>.</description></item>
        ///         <item><term>
        ///             <c>"*.example.com/*?k=v"</c></term>
        ///         <description>
        ///             Sets <see cref="Protocol"/> to <see cref="RedirectProtocol.Keep"/>, <see cref="Domain"/> to
        ///             <c>"example.com"</c>, <see cref="PrependSubdomain"/> to <c>true</c>, <see cref="Path"/> to <c>"/"</c>,
        ///             <see cref="AppendSubpath"/> to <c>true</c>, <see cref="QueryBehavior"/> to <see
        ///             cref="RedirectQueryParameters.Replace"/> and <see cref="QueryParameters"/> to a collection containing
        ///             only the key <c>"k"</c> with the value <c>"v"</c>.</description></item>
        ///         <item><term>
        ///             <c>"?*&amp;k=v&amp;k2=v2"</c></term>
        ///         <description>
        ///             Sets <see cref="QueryBehavior"/> to <see cref="RedirectQueryParameters.Append"/> and <see
        ///             cref="QueryParameters"/> to a collection containing the key/value pairs <c>"k","v"</c> and
        ///             <c>"k2","v2"</c>. All other settings are left unchanged.</description></item></list></example>
        public string AutoConfig = null;

        /// <summary>Specifies how to deal with the protocol.</summary>
        [ClassifyEnforceEnum]
        public RedirectProtocol Protocol = RedirectProtocol.Keep;

        /// <summary>What to change the URL’s domain to, or <c>null</c> to keep it unchanged.</summary>
        public string Domain = null;

        /// <summary>
        ///     Keeps the subdomain from the incoming URL. The “subdomain” is the part of the domain that precedes the URL
        ///     hook.</summary>
        public bool PrependSubdomain = true;

        /// <summary>What to change the URL’s path to, or <c>null</c> to keep it unchanged.</summary>
        public string Path = null;

        /// <summary>
        ///     Appends the subpath from the incoming URL. The “subpath” is the part of the path that follows the URL hook.</summary>
        public bool AppendSubpath = true;

        /// <summary>Specifies how Redirector should deal with the query parameters.</summary>
        [ClassifyEnforceEnum]
        public RedirectQueryParameters QueryBehavior = RedirectQueryParameters.Keep;

        /// <summary>Specifies query parameters to be used in a way described by <see cref="QueryBehavior"/>.</summary>
        [ClassifyNotNull]
        public KeyValuePair<string, string>[] QueryParameters = { };

        void IClassifyObjectProcessor.BeforeSerialize() { }

        void IClassifyObjectProcessor.AfterDeserialize()
        {
            if (AutoConfig != null)
            {
                // Parse the AutoConfig
                var posQ = AutoConfig.IndexOf('?');
                if (posQ != -1)
                {
                    var q = AutoConfig.Substring(posQ);
                    if (q.StartsWith("?*&"))
                    {
                        QueryBehavior = RedirectQueryParameters.Append;
                        q = "?" + q.Substring(3);
                    }
                    else if (q.EndsWith("&*"))
                    {
                        QueryBehavior = RedirectQueryParameters.Prepend;
                        q = q.Remove(q.Length - 2);
                    }
                    else
                        QueryBehavior = RedirectQueryParameters.Replace;

                    QueryParameters = HttpHelper.ParseQueryString(q).ToArray();
                    AutoConfig = AutoConfig.Remove(posQ);
                }

                if (AutoConfig.EndsWith("/*"))
                {
                    AppendSubpath = true;
                    AutoConfig = AutoConfig.Remove(AutoConfig.Length - 2);
                }
                else if (AutoConfig.Length > 0)
                    AppendSubpath = false;

                if (AutoConfig.StartsWith("http://"))
                {
                    Protocol = RedirectProtocol.Http;
                    AutoConfig = AutoConfig.Substring(7);
                }
                else if (AutoConfig.StartsWith("https://"))
                {
                    Protocol = RedirectProtocol.Https;
                    AutoConfig = AutoConfig.Substring(8);
                }
                else if (AutoConfig.Length > 0)
                    Protocol = RedirectProtocol.Keep;

                if (AutoConfig.Length > 0 && !AutoConfig.StartsWith("/"))
                {
                    if (AutoConfig.StartsWith("*."))
                    {
                        PrependSubdomain = true;
                        AutoConfig = AutoConfig.Substring(2);
                    }
                    else
                        PrependSubdomain = false;
                    var posSlash = AutoConfig.IndexOf('/');
                    Domain = posSlash == -1 ? AutoConfig : AutoConfig.Remove(posSlash);
                    AutoConfig = posSlash == -1 ? "" : AutoConfig.Substring(posSlash);
                }

                if (AutoConfig.Length > 0)
                    Path = AutoConfig;
                AutoConfig = null;
            }
            else if (Path != null)
            {
                // Take query parameters from the Path and put them into QueryParameters
                var posQP = Path.IndexOf('?');
                if (posQP != -1)
                {
                    QueryParameters = HttpHelper.ParseQueryString(Path.Substring(posQP)).Concat(QueryParameters).ToArray();
                    Path = Path.Remove(posQP);
                }
            }
        }
    }
}
