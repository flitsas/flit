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
/// SuperAdmin pidió correr sobre «todas las compañías» sin un filtro de
/// <c>CompanyQueryFieldCatalog.Compania</c> ni un rango de fecha acotado.
///
/// <para>Sin una de las dos cosas, la consulta barre la plataforma entera contra un tope de cordura
/// (<see cref="QueryLimits.MaxUniverso"/>) que se aplica SIN <c>ORDER BY</c>: el usuario vería una
/// porción arbitraria del universo y lo leería como el resultado completo. Es preferible pedir que
/// acote antes de correr, a devolver un resultado que se parece a la respuesta pero no lo es.</para>
/// </summary>
public sealed class SuperAdminQueryTooBroadException : Exception
{
    public SuperAdminQueryTooBroadException(int maxDias)
        : base(
            "Esta consulta cruza todas las compañías sin acotar: elige una o varias compañías, "
            + $"o un rango de fecha de máximo {maxDias} días.") =>
        MaxDias = maxDias;

    public SuperAdminQueryTooBroadException()
        : this(QueryLimits.MaxDiasSinAcotarCompania)
    {
    }

    public int MaxDias { get; }
}
