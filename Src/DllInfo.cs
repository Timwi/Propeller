using System;
using RT.PropellerApi;

namespace RT.Propeller
{
    /// <summary>Contains information about a loaded DLL.</summary>
    [Serializable]
    sealed class DllInfo
    {
        /// <summary>Reference to the instantiated Propeller module.</summary>
        public IPropellerModule Module;

        /// <summary>Caches the result of <see cref="IPropellerModule.GetName"/>.</summary>
        public string ModuleName;

        /// <summary>Path to the original DLL in the plugins folder. We are not supposed to touch the DLL file itself, but we may need to know its path.</summary>
        public string OrigDllPath;

        /// <summary>Path to the DLL in the temp folder where <see cref="AppDomainRunner"/> has copied it.</summary>
        public string TempDllPath;
    }
}
