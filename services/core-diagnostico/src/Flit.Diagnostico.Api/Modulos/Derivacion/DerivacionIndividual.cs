using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Catalogos;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Personas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>Cuerpo de <c>POST …/vehiculos/{vid}/tramite</c>: el tipo solo cuenta en un lote Simple.</summary>
public sealed record CrearTramiteEntrada([property: System.Text.Json.Serialization.JsonPropertyName("tipo_tramite_codigo")] string? TipoTramiteCodigo);

/// <summary>
/// «Crear trámite» de una placa, síncrono (Punto 4, diseño §5.1 fila 1, §7 primer diagrama, decisión 4.6):
/// <list type="number">
/// <item>Lote visible para el usuario (<see cref="AccesoLote"/>, 404 si no) y vehículo de ese lote (404).</item>
/// <item>Con trámite: 409 YA_TIENE_TRAMITE, sin llamar a Trámites.</item>
/// <item>Tipo: el del lote (Con análisis) o el del cuerpo (Simple; 400 TIPO_TRAMITE_REQUERIDO sin él, 400 TIPO_TRAMITE_INVALIDO si no
/// está en el catálogo de la etapa; 503 CATALOGO_NO_DISPONIBLE si el catálogo no responde).</item>
/// <item>Revalidación (<see cref="LecturaElegibilidad"/>): 422 NO_ELEGIBLE con el motivo.</item>
/// <item>Operación INDIVIDUAL + ítem ENVIADO en una transacción; un segundo ítem vivo del vehículo (doble clic) choca con
/// <c>ux_derivaciones_vehiculo_vivo</c> y responde 422 EN_CURSO.</item>
/// <item>Rpc con la empresa del lote y el autor; sin el token de la petición (si el cliente corta, el ítem igual se cierra).</item>
/// <item><see cref="CierreItemDerivacion"/> y respuesta (<see cref="RespuestaDerivacion"/>).</item>
/// </list>
/// Logs: ids y códigos; nunca placa ni documento.
/// </summary>
internal sealed partial class DerivacionIndividual(
    DiagnosticoDb db,
    UsuarioActual usuario,
    IClienteDerivacionTramites tramites,
    ICatalogoTiposTramite catalogo,
    IProtectorDocumentoPersona protector,
    IOptions<OpcionesDerivacion> opciones,
    FrenoTramites freno,
    TimeProvider reloj,
    ILogger<DerivacionIndividual> logger)
{
    public const string CodigoTipoRequerido = ApoyoDerivacion.CodigoTipoRequerido;
    public const string CodigoTipoInvalido = ApoyoDerivacion.CodigoTipoInvalido;
    public const string CampoTipo = ApoyoDerivacion.CampoTipo;

    public async Task<IResult> CrearAsync(Guid id, Guid vid, CrearTramiteEntrada? entrada, CancellationToken ct)
    {
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var vehiculo = await db.Set<VehiculoDiagnostico>().SingleOrDefaultAsync(v => v.Id == vid && v.DiagnosticoId == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El vehículo");
        if (vehiculo.TramiteId is { } tramite)
            return RespuestaDerivacion.YaTieneTramite(tramite, vehiculo.TramiteNumero ?? string.Empty);

        var tipo = await ApoyoDerivacion.TipoAsync(catalogo, cabecera.Modo, cabecera.TipoTramiteCodigo, entrada?.TipoTramiteCodigo, ct).ConfigureAwait(false);
        var elegibilidad = (await LecturaElegibilidad.LeerAsync(db, id, cabecera.Modo, [vid], ct).ConfigureAwait(false))[vid];
        if (elegibilidad.Motivo is { } motivo)
            return RespuestaDerivacion.NoElegible(motivo);

        // Incremento 3: con el freno abierto (Trámites falló varias veces seguidas) no se llama; 503 con lo que falta de la pausa.
        if (freno.RetryAfterSegundos() is { } espera)
            return RespuestaDerivacion.NoDisponible(espera);

        var ahora = reloj.GetUtcNow();
        var operacion = OperacionDerivacion.Individual(cabecera.TenantId, id, tipo, usuario.Id, ahora);
        var item = ItemDerivacion.Enviado(operacion, vid, orden: 1, ahora);
        item.Intentos = 1;
        db.Add(operacion);
        db.Add(item);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: Persistence.Configuraciones.ItemDerivacionConfig.IndiceVivo })
        {
            return RespuestaDerivacion.NoElegible(MotivoDerivacion.EnCurso);
        }

        var solicitud = MapeoBorradorTramite.Armar(vehiculo, tipo, usuario.Id, item.Id, await ApoyoDerivacion.OrganismoCodigoAsync(db, vehiculo.OrganismoNombre, ct).ConfigureAwait(false), protector);
        var resultado = await tramites.CrearAsync(cabecera.TenantId, solicitud, CancellationToken.None).ConfigureAwait(false);
        switch (resultado)
        {
            case ResultadoBorrador.Creado or ResultadoBorrador.Rechazado:
                freno.RegistrarExito();
                break;
            case ResultadoBorrador.NoDisponible { Transitorio: true }:
                freno.RegistrarFalloTransitorio();
                break;
        }

        // HU #14563 (ADR-0086 D-1): cierre y versión de fila en una transacción; la cabecera se bloquea primero.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            var version = item.Estado is EstadoItemDerivacion.Enviado
                ? await CierreItemDerivacion.CerrarAsync(db, operacion, item, vehiculo, resultado, reloj.GetUtcNow(), CancellationToken.None).ConfigureAwait(false)
                : await Consultas.EscritorCeldas.SubirVersionFilaAsync(db, id, vehiculo.Id, CancellationToken.None).ConfigureAwait(false);
            await GuardarCierreAsync(vehiculo, version).ConfigureAwait(false);
            await transaccion.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
        LogCierre(logger, item.Id, vid, item.Estado, item.MotivoCodigo);

        var tramiteFinal = vehiculo.TramiteId ?? (resultado as ResultadoBorrador.Creado)?.TramiteId ?? Guid.Empty;
        return RespuestaDerivacion.Desde(resultado, opciones.Value.UrlTramite(tramiteFinal, cabecera.TenantId));
    }

    /// <summary>
    /// Guarda el cierre. Si el procesador tocó el vehículo mientras se llamaba a Trámites (xmin cambió), se relee y se vuelve a
    /// fijar el trámite una vez: el ítem no puede quedar ENVIADO (bloquearía el vehículo con EN_CURSO). La recarga trae la versión
    /// de fila de la base: se vuelve a marcar con la de este cierre (HU #14563).
    /// </summary>
    private async Task GuardarCierreAsync(VehiculoDiagnostico vehiculo, long versionFila)
    {
        try
        {
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            var entrada = db.Entry(vehiculo);
            var (tramite, numero, creadoEn) = (vehiculo.TramiteId, vehiculo.TramiteNumero, vehiculo.TramiteCreadoEn);
            await entrada.ReloadAsync(CancellationToken.None).ConfigureAwait(false);
            if (vehiculo.TramiteId is null && tramite is not null)
            {
                vehiculo.TramiteId = tramite;
                vehiculo.TramiteNumero = numero;
                vehiculo.TramiteCreadoEn = creadoEn;
            }

            Consultas.EscritorCeldas.MarcarFila(vehiculo, versionFila);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Derivación individual: ítem {ItemId} del vehículo {VehiculoId} cerró en {Estado} {Motivo}.")]
    private static partial void LogCierre(ILogger logger, Guid itemId, Guid vehiculoId, EstadoItemDerivacion estado, string? motivo);
}
