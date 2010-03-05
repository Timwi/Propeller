using System.Collections.Generic;
using RT.Servers;
using RT.Util;

namespace RT.PropellerApi
{
    /// <summary>Implement this interface to create a module for Propeller, the flexible HTTP webserver.</summary>
    public interface IPropellerModule
    {
        /// <summary>When implemented in a class, returns a human-readable name of the plugin.</summary>
        string GetName();

        /// <summary>When implemented in a class, initialises the module.</summary>
        /// <remarks>Every time the Propeller server re-initialises, it instantiates the class and then calls this method.</remarks>
        /// <param name="origDllPath">Path to the DLL file in the Propeller plugins directory.</param>
        /// <param name="tempDllPath">Path to the temporary copy of the DLL which is actually being executed.</param>
        /// <param name="log">Reference to a <see cref="LoggerBase"/> that should be used to log messages. Use a lock(log) statement around every call to this object.</param>
        /// <returns>An instance of <see cref="PropellerModuleInitResult"/> containing information about how Propeller should proceed.</returns>
        PropellerModuleInitResult Init(string origDllPath, string tempDllPath, LoggerBase log);

        /// <summary>When implemented in a class, determines whether the server requires re-initialisation.</summary>
        /// <remarks>The Propeller engine periodically calls this method on all active modules.</remarks>
        bool MustReinitServer();

        /// <summary>When implemented in a class, shuts down the module.</summary>
        /// <remarks>
        ///    <para>The Propeller engine calls this method on all active modules when the server is re-initialised. The instance is discarded after the call; all future calls go to the newly-initialised instance.</para>
        ///    <para>This method may not be called if the Propeller engine is interrupted via a service stop or a Ctrl-C on the console.</para>
        /// </remarks>
        void Shutdown();
    }

    /// <summary>Contains information returned by an implementation of <see cref="IPropellerModule.Init"/>, which is passed to the Propeller engine.</summary>
    public class PropellerModuleInitResult
    {
        /// <summary>Contains a list of request handler hooks which the module provides. Propeller will pass these to the HTTP server.</summary>
        public IEnumerable<HttpRequestHandlerHook> HandlerHooks;
        /// <summary>Contains a list of file filters (e.g. C:\path\*.*) which Propeller will monitor for changes. Every time a change to any matching file is detected,
        /// Propeller will completely re-initialise itself and all modules.</summary>
        public IEnumerable<string> FileFiltersToBeMonitoredForChanges;
    }
}
