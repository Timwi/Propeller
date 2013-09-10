using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RT.Propeller
{
    [Serializable]
    sealed class ModuleInitializationException : Exception
    {
        public ModuleInitializationException() : this(null, null) { }
        public ModuleInitializationException(Exception inner) : this(null, inner) { }
        public ModuleInitializationException(string message) : this(message, null) { }
        public ModuleInitializationException(string message, Exception inner) : base(message, inner) { }
    }
}
