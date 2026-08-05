namespace Flit.Admin.Domain.OtQueries;

/// <summary>
/// El usuario llegó al tope de consultas guardadas.
///
/// <para>Es una excepción y no un <c>null</c> porque <c>null</c> ya significa otra cosa en este
/// repositorio —«el tenant no tiene organismo»— y confundir las dos respondería un 404 con un
/// mensaje sobre la configuración del perfil OT a alguien que solo intentaba guardar una consulta.</para>
/// </summary>
public sealed class OtSavedQueryLimitException : Exception
{
    public OtSavedQueryLimitException(int limit)
        : base($"Se alcanzó el máximo de {limit} consultas guardadas. Borre alguna para guardar otra.") =>
        Limit = limit;

    public OtSavedQueryLimitException()
        : this(OtQueryLimits.MaxConsultasGuardadas)
    {
    }

    public OtSavedQueryLimitException(string message)
        : base(message) => Limit = OtQueryLimits.MaxConsultasGuardadas;

    public OtSavedQueryLimitException(string message, Exception innerException)
        : base(message, innerException) => Limit = OtQueryLimits.MaxConsultasGuardadas;

    public int Limit { get; }
}
