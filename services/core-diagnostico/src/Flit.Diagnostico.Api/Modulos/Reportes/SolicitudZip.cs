using System.Text.Json.Serialization;
using Flit.Diagnostico.Api.Infraestructura.Archivos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>Cuerpo de <c>POST /diagnosticos/{id}/evidencias</c>: el tipo de evidencia en segundo plano (hoy solo <c>ZIP</c>).</summary>
public sealed record SolicitudEvidenciaDto([property: JsonPropertyName("tipo")] string? Tipo);

/// <summary>
/// Estado de un ZIP del lote (HU #13850): <c>estado</c> PENDIENTE, GENERANDO, LISTA o ERROR; <c>vehiculos_incluidos</c> son los
/// vehículos con su PDF (los esperados mientras está en la cola, los del archivo cuando está LISTA) y <c>vehiculos_pendientes</c>
/// los que quedan fuera por no haber terminado (van en el índice). <c>tipo_error</c> solo en ERROR (ALMACEN_NO_DISPONIBLE,
/// GENERACION_FALLIDA, GENERACION_INTERRUMPIDA o SIN_VEHICULOS_TERMINADOS); nunca causas técnicas de las fuentes.
/// </summary>
public sealed record EstadoEvidenciaDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("vehiculos_incluidos")] int VehiculosIncluidos,
    [property: JsonPropertyName("vehiculos_pendientes")] int VehiculosPendientes,
    [property: JsonPropertyName("creado_en")] DateTimeOffset CreadoEn,
    [property: JsonPropertyName("generado_en")] DateTimeOffset? GeneradoEn,
    [property: JsonPropertyName("tipo_error")] string? TipoError)
{
    public static EstadoEvidenciaDto De(Evidencia evidencia)
    {
        ArgumentNullException.ThrowIfNull(evidencia);
        return new EstadoEvidenciaDto(
            evidencia.Id, CodigosDeEnum.Codigo(evidencia.Tipo), CodigosDeEnum.Codigo(evidencia.Estado), evidencia.VehiculosIncluidos,
            evidencia.VehiculosPendientes, evidencia.CreadoEn, evidencia.GeneradoEn, evidencia.TipoError);
    }
}

/// <summary>Resultado del pedido: el estado del ZIP y si ya estaba LISTA con la misma huella (200) o queda en la cola (202).</summary>
internal sealed record PedidoZip(EstadoEvidenciaDto Estado, bool Listo);

/// <summary>
/// Caso de uso HTTP del ZIP del lote (HU #13850, F07-H5, decisión Q2): siempre con la visibilidad de <see cref="AccesoLote"/> (otra
/// compañía, PROPIOS de otro usuario, o una evidencia de otro lote: 404 sin datos, antes que cualquier 409 o 422).
/// <list type="bullet">
/// <item><see cref="PedirAsync"/>: con el plan de hoy (<see cref="PlanZip"/>), si el ZIP vigente está LISTA con la misma huella lo
/// devuelve (200, no encola); si hay uno PENDIENTE o GENERANDO con esa huella, ese (202, no duplica: dos pedidos a la vez chocan en
/// <see cref="IndiceEnCola"/> y el perdedor entrega el del ganador); si no, encola uno nuevo (202) con <c>solicitado_por</c> y la
/// empresa del claim <c>company_name</c>. 409 <see cref="CodigoSinVehiculosTerminados"/> si ningún vehículo puede emitir su PDF
/// (no crea nada); 422 <see cref="CodigoTipoInvalido"/> si <c>tipo</c> no es <c>ZIP</c>.</item>
/// <item><see cref="EstadoAsync"/> y <see cref="ArchivoAsync"/> (409 <see cref="CodigoNoLista"/> si no está LISTA; el archivo se
/// entrega como flujo del almacén, sin cargarlo entero). Un ZIP LISTA que ya no es el vigente se sigue pudiendo descargar.</item>
/// </list>
/// </summary>
internal sealed partial class SolicitudZip(
    DiagnosticoDb db, UsuarioActual usuario, LectorContenidoReporte lector, IAlmacenArchivos almacen, TimeProvider reloj, ILogger<SolicitudZip> log)
{
    public const string TipoZip = "ZIP";

    public const string CodigoTipoInvalido = "TIPO_INVALIDO";

    public const string CodigoSinVehiculosTerminados = "SIN_VEHICULOS_TERMINADOS";

    public const string CodigoNoLista = "EVIDENCIA_NO_LISTA";

    /// <summary>Un solo ZIP en la cola por lote y huella (<c>EvidenciaConfig</c>).</summary>
    public const string IndiceEnCola = "ux_evidencias_zip_en_cola";

    /// <summary>Largo de <c>evidencias.empresa_nombre</c>.</summary>
    private const int LargoEmpresa = 200;

    public async Task<PedidoZip> PedirAsync(Guid diagnosticoId, string? tipo, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        if (!string.Equals(tipo, TipoZip, StringComparison.Ordinal))
            throw new ReglaException(CodigoTipoInvalido, "Solo el ZIP de los PDF del lote se pide en segundo plano: tipo debe ser ZIP.") { Campo = "tipo" };

        var empresa = Empresa(usuario.EmpresaNombre);
        var plan = await PlanZip.LeerAsync(db, lector, cabecera, empresa, ct).ConfigureAwait(false);
        if (plan.Incluidos.Count == 0)
        {
            throw ReglaException.Conflicto(
                CodigoSinVehiculosTerminados, "Ningún vehículo del lote ha terminado todavía: el ZIP se puede pedir cuando termine al menos uno.");
        }

        var zips = Zips(cabecera.Id);
        if (await zips.SingleOrDefaultAsync(e => e.Vigente, ct).ConfigureAwait(false) is { Estado: EstadoEvidencia.Lista } lista && lista.Huella == plan.Huella)
            return new PedidoZip(EstadoEvidenciaDto.De(lista), Listo: true);
        if (await EnColaAsync(cabecera.Id, plan.Huella, ct).ConfigureAwait(false) is { } enCola)
            return new PedidoZip(EstadoEvidenciaDto.De(enCola), Listo: false);

        var nuevo = new Evidencia
        {
            DiagnosticoId = cabecera.Id,
            Tipo = TipoEvidencia.Zip,
            Huella = plan.Huella,
            Estado = EstadoEvidencia.Pendiente,
            // Nace no vigente: el ZIP LISTA anterior se sigue entregando hasta que este quede LISTA.
            Vigente = false,
            SolicitadoPor = usuario.Id,
            EmpresaNombre = empresa,
            Intentos = 0,
            CreadoEn = reloj.GetUtcNow(),
            VehiculosIncluidos = plan.Incluidos.Count,
            VehiculosPendientes = plan.Pendientes,
        };
        db.Add(nuevo);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndiceEnCola })
        {
            // Otro pedido simultáneo encoló primero el mismo ZIP: se entrega el suyo.
            db.ChangeTracker.Clear();
            var ganador = await EnColaAsync(cabecera.Id, plan.Huella, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("El ZIP en cola que ganó el pedido simultáneo ya no está en la cola.", ex);
            return new PedidoZip(EstadoEvidenciaDto.De(ganador), Listo: false);
        }

        Encolado(log, nuevo.Id, cabecera.Id, plan.Incluidos.Count, plan.Pendientes);
        return new PedidoZip(EstadoEvidenciaDto.De(nuevo), Listo: false);
    }

    public async Task<EstadoEvidenciaDto> EstadoAsync(Guid diagnosticoId, Guid evidenciaId, CancellationToken ct) =>
        EstadoEvidenciaDto.De(await LeerAsync(diagnosticoId, evidenciaId, ct).ConfigureAwait(false));

    /// <summary>El ZIP LISTA como flujo y su nombre de descarga; 409 <see cref="CodigoNoLista"/> si no está LISTA.</summary>
    public async Task<(Stream Contenido, string Nombre)> ArchivoAsync(Guid diagnosticoId, Guid evidenciaId, CancellationToken ct)
    {
        var evidencia = await LeerAsync(diagnosticoId, evidenciaId, ct).ConfigureAwait(false);
        if (evidencia is not { Estado: EstadoEvidencia.Lista, ArchivoRef: { } referencia })
            throw ReglaException.Conflicto(CodigoNoLista, "El ZIP todavía no está listo: consulta su estado y descárgalo cuando esté LISTA.");
        var contenido = await almacen.AbrirAsync(referencia, ct).ConfigureAwait(false) ?? throw ReglaException.NoEncontrado("El archivo del ZIP");
        return (contenido, EmpaquetadorZip.NombreArchivo(evidencia.DiagnosticoId));
    }

    /// <summary>La evidencia ZIP <paramref name="evidenciaId"/> del lote visible <paramref name="diagnosticoId"/>; 404 si no.</summary>
    private async Task<Evidencia> LeerAsync(Guid diagnosticoId, Guid evidenciaId, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        return await Zips(cabecera.Id).SingleOrDefaultAsync(e => e.Id == evidenciaId, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El ZIP");
    }

    private IQueryable<Evidencia> Zips(Guid diagnosticoId) =>
        db.Set<Evidencia>().AsNoTracking().Where(e => e.DiagnosticoId == diagnosticoId && e.VehiculoId == null && e.Tipo == TipoEvidencia.Zip);

    private Task<Evidencia?> EnColaAsync(Guid diagnosticoId, string huella, CancellationToken ct) =>
        Zips(diagnosticoId)
            .Where(e => (e.Estado == EstadoEvidencia.Pendiente || e.Estado == EstadoEvidencia.Generando) && e.Huella == huella)
            .SingleOrDefaultAsync(ct);

    /// <summary>El nombre de la empresa del claim tal como lo guarda la evidencia (sin espacios en los extremos, hasta 200).</summary>
    private static string? Empresa(string? nombre)
    {
        var limpio = nombre?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio.Length <= LargoEmpresa ? limpio : limpio[..LargoEmpresa];
    }

    [LoggerMessage(EventId = 138510, Level = LogLevel.Information,
        Message = "ZIP {EvidenciaId} del diagnóstico {DiagnosticoId} encolado: {Incluidos} vehículos con PDF, {Pendientes} fuera")]
    private static partial void Encolado(ILogger logger, Guid evidenciaId, Guid diagnosticoId, int incluidos, int pendientes);
}
