namespace Flit.Queries.Domain;

/// <summary>
/// El usuario llegó al tope de consultas guardadas.
///
/// <para>Es una excepción y no un <c>null</c> porque <c>null</c> ya significa otra cosa en estos
/// repositorios —«este tenant no tiene el módulo configurado»— y confundir las dos respondería un
/// 404 sobre la configuración del perfil a alguien que solo intentaba guardar una consulta.</para>
/// </summary>
public sealed class SavedQueryLimitException : Exception
{
    public SavedQueryLimitException(int limit)
        : base($"Se alcanzó el máximo de {limit} consultas guardadas. Borre alguna para guardar otra.") =>
        Limit = limit;

    public SavedQueryLimitException()
        : this(QueryLimits.MaxConsultasGuardadas)
    {
    }

    public SavedQueryLimitException(string message)
        : base(message) => Limit = QueryLimits.MaxConsultasGuardadas;

    public SavedQueryLimitException(string message, Exception innerException)
        : base(message, innerException) => Limit = QueryLimits.MaxConsultasGuardadas;

    public int Limit { get; }
}

/// <summary>
/// Ya existe una consulta con ese nombre para ese usuario en ese ámbito.
///
/// <para>Se avisa en vez de renombrar sola («Rechazados (2)»): quien guarda con un nombre repetido
/// casi siempre quería SOBRESCRIBIR la que ya tenía, y crearle una copia silenciosa le deja dos
/// consultas indistinguibles en la lista.</para>
/// </summary>
public sealed class SavedQueryNameTakenException : Exception
{
    public SavedQueryNameTakenException(string nombre)
        : base($"Ya tiene una consulta guardada llamada «{nombre}».") => Nombre = nombre;

    public SavedQueryNameTakenException()
        : base("Ya tiene una consulta guardada con ese nombre.") => Nombre = string.Empty;

    public SavedQueryNameTakenException(string message, Exception innerException)
        : base(message, innerException) => Nombre = string.Empty;

    public string Nombre { get; }
}
