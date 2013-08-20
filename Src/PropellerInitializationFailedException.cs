using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RT.Propeller
{
    sealed class PropellerInitializationFailedException : Exception
    {
        public PropellerInitializationFailedException() : this(null, null) { }
        public PropellerInitializationFailedException(Exception inner) : this(null, inner) { }
        public PropellerInitializationFailedException(string message) : this(message, null) { }
        public PropellerInitializationFailedException(string message, Exception inner) : base(message, inner) { }
    }
}
