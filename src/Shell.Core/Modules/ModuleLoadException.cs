namespace Shell.Core.Modules;

/// <summary>Thrown when the module set is invalid. The host must refuse to start.</summary>
public sealed class ModuleLoadException(string message, Exception? innerException = null)
    : Exception(message, innerException);
