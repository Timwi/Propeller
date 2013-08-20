using System.Collections.Generic;
using RT.Servers;

namespace RT.PropellerApi
{
    /// <summary>
    ///     Contains information returned by an implementation of <see cref="IPropellerModule.Init"/>, which is passed to the
    ///     Propeller engine.</summary>
    public class PropellerModuleInitResult
    {
        /// <summary>
        ///     Contains a list of URL mappings which the module provides. Propeller will pass these to a <see
        ///     cref="UrlResolver"/>.</summary>
        public IEnumerable<UrlMapping> UrlMappings;

        /// <summary>
        ///     Contains a list of file filters (e.g. <c>C:\path\*.*</c>) which Propeller will monitor for changes. Every time
        ///     a change to any matching file is detected, Propeller will completely re-initialise itself and all modules.</summary>
        public IEnumerable<string> FileFiltersToBeMonitoredForChanges;
    }
}
