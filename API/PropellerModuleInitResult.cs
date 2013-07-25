using System.Collections.Generic;
using RT.Servers;

namespace RT.PropellerApi
{
    /// <summary>Contains information returned by an implementation of <see cref="IPropellerModule.Init"/>, which is passed to the Propeller engine.</summary>
    public class PropellerModuleInitResult
    {
        /// <summary>Contains a list of URL path hooks which the module provides. Propeller will pass these to a URL path resolver.</summary>
        public IEnumerable<UrlPathHook> UrlPathHooks;
        /// <summary>Contains a list of file filters (e.g. C:\path\*.*) which Propeller will monitor for changes. Every time a change to any matching file is detected,
        /// Propeller will completely re-initialise itself and all modules.</summary>
        public IEnumerable<string> FileFiltersToBeMonitoredForChanges;
    }
}
