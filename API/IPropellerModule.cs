using System.Collections.Generic;
using RT.Servers;
using RT.Util;
using RT.Util.Json;

namespace RT.PropellerApi
{
    /// <summary>Implement this interface to create a module for Propeller, the flexible HTTP webserver.</summary>
    public interface IPropellerModule
    {
        /// <summary>When implemented in a class, gets a human-readable name of the module.</summary>
        string Name { get; }

        /// <summary>
        ///     When implemented in a class, initializes the module.</summary>
        /// <remarks>
        ///     <para>
        ///         Every time the Propeller server initializes this module, it creates a new instance of the class and then
        ///         calls this method before invoking anything else on this interface.</para>
        ///     <para>
        ///         If this method throws an exception, Propeller cancels reinitialization and does not invoke any other
        ///         methods or properties on this interface.</para></remarks>
        /// <param name="log">
        ///     Reference to a <see cref="LoggerBase"/> that should be used to log messages.</param>
        /// <param name="settings">
        ///     The module’s settings as stored within the Propeller settings file.</param>
        /// <param name="saver">
        ///     An object that can be used to save the module’s settings.</param>
        void Init(LoggerBase log, JsonValue settings, ISettingsSaver saver);

        /// <summary>
        ///     When implemented in a class, returns a list of file filters (e.g. <c>C:\path\*.*</c>) which Propeller will
        ///     monitor for changes. Every time a change to any matching file is detected, Propeller reinitializes this
        ///     module.</summary>
        /// <remarks>
        ///     <para>
        ///         Propeller invokes this property only once, after calling <see cref="Init"/>.</para>
        ///     <para>
        ///         It is acceptable to return <c>null</c> in place of an empty collection.</para></remarks>
        string[] FileFiltersToBeMonitoredForChanges { get; }

        /// <summary>
        ///     When implemented in a class, handles an HTTP request.</summary>
        /// <param name="req">
        ///     HTTP request to handle.</param>
        /// <returns>
        ///     HTTP response to return to the client.</returns>
        /// <remarks>
        ///     Propeller does not invoke this method before calling <see cref="Init"/>.</remarks>
        HttpResponse Handle(HttpRequest req);

        /// <summary>
        ///     When implemented in a class, determines whether the module requires reinitialization.</summary>
        /// <remarks>
        ///     <para>
        ///         The Propeller engine periodically calls this method on all active modules.</para>
        ///     <para>
        ///         Propeller does not invoke this property before calling <see cref="Init"/>.</para></remarks>
        bool MustReinitialize { get; }

        /// <summary>
        ///     When implemented in a class, shuts down the module.</summary>
        /// <remarks>
        ///     <para>
        ///         The Propeller engine calls this method on a module when it is reinitialized. The instance is discarded
        ///         after the call; all future calls go to the newly-initialized instance.</para>
        ///     <para>
        ///         This method might not be called if the Propeller engine is interrupted via a service stop or a Ctrl-C on
        ///         the console.</para>
        ///     <para>
        ///         Propeller does not invoke this method before calling <see cref="Init"/>.</para></remarks>
        void Shutdown();
    }
}
