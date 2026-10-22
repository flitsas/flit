namespace Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;

/// <summary>
/// Qué se consulta en SIMIT (HU #13793): la placa del vehículo (celda SIMIT_VEHICULO) o el documento de una persona (celdas
/// SIMIT_PROPIETARIO y SIMIT_LOCATARIO, F05-H5), nunca los dos. El documento solo vive en memoria durante la llamada
/// (<see cref="DocumentoPersona"/>, que enmascara su número en <see cref="ToString"/>).
/// </summary>
/// <example>
/// <code>
/// var porPlaca = SolicitudSimit.PorPlaca("TST102");
/// var porPersona = SolicitudSimit.PorDocumento(new DocumentoPersona("NIT", "999990305")); // EsJuridica = true
/// </code>
/// </example>
public sealed record SolicitudSimit
{
    private SolicitudSimit(string? placa, DocumentoPersona? documento)
    {
        Placa = placa;
        Documento = documento;
    }

    /// <summary>Placa definitiva que dejó el RUNT; null si la consulta es por persona.</summary>
    public string? Placa { get; }

    /// <summary>Documento de la persona en claro; null si la consulta es por placa.</summary>
    public DocumentoPersona? Documento { get; }

    /// <summary>Verdadero si se consulta a una persona jurídica (NIT): <c>tipo_persona=JURIDICA</c> en core-consultas.</summary>
    public bool EsJuridica => Documento?.EsNit == true;

    public static SolicitudSimit PorPlaca(string placa)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placa);
        return new SolicitudSimit(placa.Trim().ToUpperInvariant(), null);
    }

    public static SolicitudSimit PorDocumento(DocumentoPersona documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        return new SolicitudSimit(null, documento);
    }

    /// <summary>Sin el documento: un log o una excepción nunca lo filtran.</summary>
    public override string ToString() => Placa is not null ? $"placa {Placa}" : $"documento {Documento}";
}

/// <summary>
/// Puerto de SIMIT (HU #13793, F05-C1, F05-C6, F05-C9): comparendos de una placa o de una persona con
/// <c>ConsultasService.ConsultarMultas</c> de core-consultas, a nombre de la empresa de la celda; el producto no elige proveedor
/// (lo decide core-consultas con la configuración de la empresa). Devuelve <see cref="ResultadoSimit"/> (semáforo, cantidad y
/// número, fecha, valor, estado y organismo de cada comparendo) o el error tipado. Implementaciones: <see cref="FuenteSimitReal"/>,
/// <see cref="FuenteSimitSimulada"/> (LOCAL o DEV) y <see cref="FuenteSimitNoConectada"/>, elegidas por
/// <see cref="SeleccionAdaptadores"/>. Lo usan <see cref="EjecutorCeldaSimitVehiculo"/> (por placa) y
/// <see cref="EjecutorCeldaSimitPersona"/> (HU #13794, por documento de cada persona).
/// </summary>
/// <example>
/// <code>
/// var r = await fuente.ConsultarAsync(celda.TenantId, SolicitudSimit.PorPlaca(placa), ct);
/// if (r.Error is { } error) return ResultadoCelda.Error(error.TipoError);
/// </code>
/// </example>
public interface IFuenteSimit
{
    Task<RespuestaFuente<ResultadoSimit>> ConsultarAsync(Guid empresa, SolicitudSimit solicitud, CancellationToken ct);
}
