namespace RT.Propeller
{
    [Serializable]
    class ModuleInitializationException : Exception
    {
        public ModuleInitializationException() : this(null, null) { }
        public ModuleInitializationException(Exception inner) : this(null, inner) { }
        public ModuleInitializationException(string message) : this(message, null) { }
        public ModuleInitializationException(string message, Exception inner) : base(message, inner) { }
    }
}
