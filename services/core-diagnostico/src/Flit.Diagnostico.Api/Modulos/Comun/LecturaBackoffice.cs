namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Una lectura entre empresas hecha por el backoffice FLIT (tabla <c>diagnostico.lecturas_backoffice</c>, HU #13722; la
/// escribe cada ruta <c>/backoffice/*</c> desde F00.1-H5, patrón ADR-0069). Solo se inserta: no se edita ni se borra.
/// <see cref="TenantId"/> es la empresa del lector, no la consultada.
/// </summary>
public sealed class LecturaBackoffice : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Empresa del lector (la del token del backoffice).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Empresa cuyos datos se leyeron (<c>?empresa=</c>).</summary>
    public Guid EmpresaConsultada { get; init; }

    /// <summary>Usuario que leyó (claim <c>sub</c>).</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Ruta y consulta leídas, sin cuerpo.</summary>
    public required string Ruta { get; init; }

    /// <summary>Momento de la lectura; si no se indica, lo pone la base.</summary>
    public DateTimeOffset Fecha { get; init; }
}
