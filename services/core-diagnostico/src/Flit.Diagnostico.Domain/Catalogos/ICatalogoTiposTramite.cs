namespace Flit.Diagnostico.Domain.Catalogos;

/// <summary>
/// Catálogo de tipos de trámite de la etapa (HU #13742): los publicados por Trámites, filtrados por la lista blanca. Si
/// Trámites no responde y no hay una copia vigente, lanza <see cref="CatalogoTiposTramiteNoDisponibleException"/>; nunca
/// devuelve una lista fija ni una copia vencida.
/// </summary>
public interface ICatalogoTiposTramite
{
    Task<IReadOnlyList<TipoTramite>> ListarAsync(CancellationToken ct);
}

/// <summary>El catálogo de tipos de trámite no está disponible (Trámites caído o cliente de servicio mal configurado).</summary>
public sealed class CatalogoTiposTramiteNoDisponibleException : Exception
{
    public CatalogoTiposTramiteNoDisponibleException()
        : base("El catálogo de tipos de trámite no está disponible.")
    {
    }

    public CatalogoTiposTramiteNoDisponibleException(string message)
        : base(message)
    {
    }

    public CatalogoTiposTramiteNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
