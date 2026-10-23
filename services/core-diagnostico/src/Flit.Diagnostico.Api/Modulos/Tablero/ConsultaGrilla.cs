using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Configuracion;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Estimacion;
using Flit.Diagnostico.Domain.Vehiculos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// De dónde lee la grilla sus filas (HU #13799): la compañía del token (<see cref="LecturaDeLaCompania"/>, filtro global por
/// empresa) o, solo desde el módulo Backoffice, la empresa explícita de la ruta <c>/backoffice/*</c>. Así la grilla del
/// backoffice reutiliza <see cref="ConsultaGrilla"/> sin que <c>IgnoreQueryFilters()</c> salga de <c>Modulos/Backoffice/</c>.
/// </summary>
internal interface ILecturaTablero
{
    /// <summary>Filas de <typeparamref name="T"/> de la empresa de la lectura, sin seguimiento de cambios.</summary>
    IQueryable<T> Filas<T>()
        where T : class, IEntidadDeEmpresa;
}

/// <summary>Lectura de la compañía del token: el filtro global por empresa de <see cref="DiagnosticoDb"/>.</summary>
internal sealed class LecturaDeLaCompania(DiagnosticoDb db) : ILecturaTablero
{
    public IQueryable<T> Filas<T>()
        where T : class, IEntidadDeEmpresa => db.Set<T>().AsNoTracking();
}

/// <summary>
/// Arma la grilla incremental del tablero (HU #13743, F04-C1/C3/C8/C10/C11/C13): con <c>desde=0</c> todo el lote; con
/// <c>desde=N</c> solo las celdas con <c>version_tablero &gt; N</c> (polling cada 3 s, ADR c) y la versión actual del lote.
/// El estado global, las columnas del alcance agrupadas y el avance van siempre. La compañía lee con el contexto de la petición
/// (filtro de la compañía del token) y la visibilidad de <see cref="AccesoLote"/>; su DTO nunca proyecta el origen, el usuario
/// manual, el payload ni los documentos (R-20). El backoffice (HU #13799) arma la misma grilla con <see cref="LeerAsync"/> y su
/// propia <see cref="ILecturaTablero"/>, y proyecta además el origen y el usuario manual de cada celda. Con
/// <c>estimado_consultas=AL_FINALIZAR</c> en la copia de configuración del lote y el lote cerrado (COMPLETADO o INCOMPLETO),
/// agrega el estimado de <see cref="EstimadorConsultas"/> (HU #13747) frente a lo consumido: intentos con <c>desde_cache=false</c>
/// que no son de llenado manual. Las fuentes de persona (HU #13796) se estiman con las filas de <c>personas_vehiculo</c> de los
/// vehículos del lote, por rol y solo de la compañía del lote; lo consumido ya es un intento por persona
/// (<c>intentos_consulta.persona_id</c>).
/// </summary>
internal sealed class ConsultaGrilla(DiagnosticoDb db, UsuarioActual usuario)
{
    public const string CodigoDesdeInvalido = "DESDE_INVALIDO";

    /// <summary>La grilla del lote desde la versión dada, para la compañía del token.</summary>
    public async Task<GrillaDto> ObtenerAsync(Guid id, long desde, CancellationToken ct)
    {
        ValidarDesde(desde);
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var grilla = await LeerAsync(new LecturaDeLaCompania(db), cabecera, desde, ct).ConfigureAwait(false);
        return new GrillaDto(
            grilla.Cabecera.Id, grilla.Cabecera.Nombre, CodigosDeEnum.Codigo(grilla.Cabecera.Estado), grilla.Cabecera.VersionTablero,
            grilla.Desde, grilla.Columnas, grilla.Avance,
            [.. grilla.Vehiculos.Select(v => new FilaVehiculoDto(
                v.Id, v.Orden, v.Identificador, v.TipoIdentificador, v.Placa, v.Vin, v.Terminado, [.. v.Celdas.Select(Dto)]))],
            grilla.Estimado);
    }

    /// <summary><c>desde</c> es la versión que ya tiene el cliente: 0 o mayor; si no, 400 <c>DESDE_INVALIDO</c>.</summary>
    public static void ValidarDesde(long desde)
    {
        if (desde < 0)
            throw new ReglaException(CodigoDesdeInvalido, "desde es la versión del tablero que ya tiene el cliente: 0 o mayor.", StatusCodes.Status400BadRequest) { Campo = "desde" };
    }

    /// <summary>
    /// Lee la grilla de <paramref name="cabecera"/> desde la versión dada con <paramref name="lectura"/> (la cabecera ya pasó el
    /// control de acceso de quien llama). Las celdas traen el origen y el usuario manual: cada llamador decide qué proyecta.
    /// </summary>
    public static async Task<GrillaLeida> LeerAsync(ILecturaTablero lectura, CabeceraDiagnostico cabecera, long desde, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lectura);
        ArgumentNullException.ThrowIfNull(cabecera);
        var id = cabecera.Id;
        var celdas = await lectura.Filas<CeldaConsulta>()
            .Where(c => c.DiagnosticoId == id)
            .Select(c => new CeldaLeida(
                c.Id, c.VehiculoId, c.Fuente, c.Estado, c.MotivoNa, c.TipoError, c.DatoObtenidoEn, c.ReutilizadaDeId != null, c.VersionTablero,
                c.Origen, c.UsuarioManualId))
            .ToListAsync(ct).ConfigureAwait(false);
        var porVehiculo = celdas.GroupBy(c => c.VehiculoId).ToDictionary(g => g.Key, g => g.ToList());
        var cambiadas = celdas.Where(c => c.Version > desde).ToList();
        var idsCambiados = cambiadas.Select(c => c.VehiculoId).Distinct().ToList();

        var vehiculos = lectura.Filas<VehiculoDiagnostico>().Where(v => v.DiagnosticoId == id);
        var totalVehiculos = await vehiculos.CountAsync(ct).ConfigureAwait(false);
        var filas = await (desde == 0 ? vehiculos : vehiculos.Where(v => idsCambiados.Contains(v.Id)))
            .OrderBy(v => v.Orden)
            .Select(v => new { v.Id, v.Orden, v.IdentificadorNorm, v.TipoIdentificador, v.Placa, v.Vin })
            .ToListAsync(ct).ConfigureAwait(false);

        var ordenFuente = cabecera.Alcance.Distinct().Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i);
        var cambiadasPorVehiculo = cambiadas.ToLookup(c => c.VehiculoId);
        var filasLeidas = filas.Select(v => new VehiculoLeido(
            v.Id, v.Orden, v.IdentificadorNorm, CodigosDeEnum.Codigo(v.TipoIdentificador), v.Placa, v.Vin,
            AvanceVehiculo.Terminado(porVehiculo.GetValueOrDefault(v.Id, []).Select(c => c.Estado)),
            [.. cambiadasPorVehiculo[v.Id].OrderBy(c => ordenFuente.GetValueOrDefault(c.Fuente, int.MaxValue))])).ToList();
        var terminados = porVehiculo.Values.Count(c => AvanceVehiculo.Terminado(c.Select(x => x.Estado)));

        return new GrillaLeida(
            cabecera, desde, AlcanceDto.De(Alcance.Agrupar(cabecera.Alcance)), new AvanceLoteDto(totalVehiculos, terminados), filasLeidas,
            await EstimadoAsync(lectura, cabecera, ct).ConfigureAwait(false));
    }

    private static CeldaGrillaDto Dto(CeldaLeida c) => new(
        c.Id, CodigosDeEnum.Codigo(c.Fuente), CodigosDeEnum.Codigo(c.Estado), c.MotivoNa is { } m ? CodigosDeEnum.Codigo(m) : null,
        c.TipoError, c.DatoObtenidoEn, c.Reutilizada, c.Version);

    /// <summary>
    /// Estimado frente a consumido solo con AL_FINALIZAR (copia de la cabecera) y el lote cerrado; null en todo otro caso. También lo
    /// usa la hoja «Consultas» del Excel del lote (HU #13849) para no duplicar el cálculo.
    /// </summary>
    internal static async Task<IReadOnlyList<EstimadoVsConsumidoDto>?> EstimadoAsync(ILecturaTablero lectura, CabeceraDiagnostico cabecera, CancellationToken ct)
    {
        if (cabecera.Estado is not (EstadoDiagnostico.Completado or EstadoDiagnostico.Incompleto) || EstimadoDeLaCopia(cabecera.Configuracion) != EstimadoConsultas.AlFinalizar)
            return null;

        var id = cabecera.Id;
        var tenant = cabecera.TenantId;
        var vehiculosDelLote = lectura.Filas<VehiculoDiagnostico>().Where(v => v.DiagnosticoId == id);
        var vehiculos = await vehiculosDelLote
            .Select(v => new { v.Id, ConLocatario = v.LocatarioDocOrigen == OrigenDocumento.DeclaradoCliente || v.LocatarioDeclaradoDocCifrado != null })
            .ToListAsync(ct).ConfigureAwait(false);
        var personas = (await lectura.Filas<PersonaVehiculo>()
                .Where(p => p.TenantId == tenant && vehiculosDelLote.Any(v => v.Id == p.VehiculoId))
                .GroupBy(p => new { p.VehiculoId, p.Rol })
                .Select(g => new { g.Key.VehiculoId, g.Key.Rol, Personas = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(x => x.VehiculoId);
        var estimado = EstimadorConsultas.Estimar(cabecera.Alcance, vehiculos.Select(v => new VehiculoAEstimar(true, v.ConLocatario,
            personas.Contains(v.Id)
                ? new PersonasRegistradas(
                    personas[v.Id].Where(x => x.Rol == RolPersona.Propietario).Sum(x => x.Personas),
                    personas[v.Id].Where(x => x.Rol == RolPersona.Locatario).Sum(x => x.Personas))
                : null)));
        var consumidas = (await lectura.Filas<IntentoConsulta>()
                .Where(i => i.DiagnosticoId == id && !i.DesdeCache && i.Disparador != DisparadorIntento.Manual)
                .GroupBy(i => i.Fuente)
                .Select(g => new { Fuente = g.Key, Consultas = g.Count() })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Fuente, x => x.Consultas);
        return [.. estimado.Select(e => new EstimadoVsConsumidoDto(CodigosDeEnum.Codigo(e.Fuente), e.Consultas, consumidas.GetValueOrDefault(e.Fuente), e.Minimo))];
    }

    /// <summary><c>estimado_consultas</c> de la copia de configuración guardada en la cabecera; sin él, <c>NO_MOSTRAR</c>.</summary>
    private static EstimadoConsultas EstimadoDeLaCopia(string configuracion)
    {
        using var copia = JsonDocument.Parse(configuracion);
        return copia.RootElement.ValueKind == JsonValueKind.Object
            && copia.RootElement.TryGetProperty("estimado_consultas", out var valor)
            && valor.ValueKind == JsonValueKind.String
            && CodigosDeEnum.TryLeer<EstimadoConsultas>(valor.GetString(), out var estimado)
                ? estimado
                : EstimadoConsultas.NoMostrar;
    }
}

/// <summary>La grilla leída (HU #13799): común a la compañía y al backoffice; cada uno la proyecta a su DTO.</summary>
internal sealed record GrillaLeida(
    CabeceraDiagnostico Cabecera, long Desde, AlcanceDto Columnas, AvanceLoteDto Avance, IReadOnlyList<VehiculoLeido> Vehiculos,
    IReadOnlyList<EstimadoVsConsumidoDto>? Estimado);

/// <summary>Un vehículo de la grilla con sus celdas cambiadas desde <c>desde</c>, en el orden del alcance.</summary>
internal sealed record VehiculoLeido(
    Guid Id, int Orden, string Identificador, string TipoIdentificador, string? Placa, string? Vin, bool Terminado, IReadOnlyList<CeldaLeida> Celdas);

/// <summary>
/// Una celda leída, con el origen del dato y el usuario del llenado manual: solo el DTO del backoffice los proyecta; el de la
/// compañía los descarta (R-20).
/// </summary>
internal sealed record CeldaLeida(
    Guid Id, Guid VehiculoId, Fuente Fuente, EstadoCelda Estado, MotivoNa? MotivoNa, string? TipoError, DateTimeOffset? DatoObtenidoEn,
    bool Reutilizada, long Version, OrigenCelda? Origen, Guid? UsuarioManualId);
