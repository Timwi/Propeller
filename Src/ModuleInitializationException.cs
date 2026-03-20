namespace RT.Propeller
{
    [Serializable]
    class ModuleInitializationException(string message, Exception inner) : Exception(message, inner)
    {
        public ModuleInitializationException() : this(null, null) { }
        public ModuleInitializationException(Exception inner) : this(null, inner) { }
        public ModuleInitializationException(string message) : this(message, null) { }
    }
}
