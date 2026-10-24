namespace Rastrear.Core;

/// <summary>
/// Error provocado por una entrada del usuario o una condición externa esperada
/// (archivo inexistente, parámetros inválidos, modelo faltante, etc.). Salida: código 1.
/// </summary>
public class RastrearUserException : Exception
{
    public RastrearUserException(string message) : base(message) { }
    public RastrearUserException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Error interno del módulo de IA, del protocolo de comunicación o del sistema. Salida: código 2.
/// </summary>
public class RastrearModuleException : Exception
{
    public RastrearModuleException(string message) : base(message) { }
    public RastrearModuleException(string message, Exception innerException) : base(message, innerException) { }
}
