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

/// <summary>
/// SuperAdmin pidió correr una consulta de SuperAdmin cuyo universo —lo que habría que cargar a
/// memoria para resolverla, antes de aplicar el resto de los filtros— supera
/// <see cref="QueryLimits.MaxUniverso"/>.
///
/// <para>El aviso lleva el conteo real de la consulta, no una regla fija de días: se calcula con un
/// <c>COUNT</c> en la base antes de cargar una sola fila, así que solo aparece cuando de verdad hace
/// falta acotar — no por adivinar de antemano que la plataforma tiene demasiados datos. Cargar ese
/// universo igual, sin acotar, correría contra un tope que se aplica SIN <c>ORDER BY</c>: el usuario
/// vería una porción arbitraria y la leería como el resultado completo.</para>
/// </summary>
public sealed class SuperAdminQueryTooBroadException : Exception
{
    public SuperAdminQueryTooBroadException(int total, int max)
        : base(
            $"Esta consulta trae unos {total:N0} trámites, más de lo que se puede traer de una vez "
            + $"(máximo {max:N0}). Acótala eligiendo una o varias compañías, o un rango de fecha más corto.")
    {
        Total = total;
        Max = max;
    }

    public SuperAdminQueryTooBroadException()
        : this(QueryLimits.MaxUniverso + 1, QueryLimits.MaxUniverso)
    {
    }

    public int Total { get; }

    public int Max { get; }
}
