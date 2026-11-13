using System.Text.Json;
using Flit.Diagnostico.Domain.Catalogos;
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
using Flit.Diagnostico.Domain.Personas;
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
/// manual, el payload ni documentos en claro (R-20): de cada fila solo el documento declarado y los propietarios, con su tipo y
/// enmascarados aquí (HU #14087). El backoffice (HU #13799) arma la misma grilla con <see cref="LeerAsync"/> y su
/// propia <see cref="ILecturaTablero"/>, y proyecta además el origen y el usuario manual de cada celda. Con
/// <c>estimado_consultas=AL_FINALIZAR</c> en la copia de configuración del lote y el lote cerrado (COMPLETADO o INCOMPLETO),
/// agrega el estimado de <see cref="EstimadorConsultas"/> (HU #13747) frente a lo consumido: intentos con <c>desde_cache=false</c>
/// que no son de llenado manual. Las fuentes de persona (HU #13796) se estiman con las filas de <c>personas_vehiculo</c> de los
/// vehículos del lote, por rol y solo de la compañía del lote; lo consumido ya es un intento por persona
/// (<c>intentos_consulta.persona_id</c>).
/// </summary>
internal sealed class ConsultaGrilla(
    DiagnosticoDb db, UsuarioActual usuario, IProtectorDocumentoPersona protector, TimeProvider reloj, Microsoft.Extensions.Options.IOptions<OpcionesConsultas> opciones,
    ICatalogoTiposTramite catalogo, IResolutorNombresUsuario nombres)
{
    public const string CodigoDesdeInvalido = "DESDE_INVALIDO";

    /// <summary>La grilla del lote desde la versión dada, para la compañía del token.</summary>
    public async Task<GrillaDto> ObtenerAsync(Guid id, long desde, CancellationToken ct)
    {
        ValidarDesde(desde);
        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, id, seguir: false, ct).ConfigureAwait(false);
        var lectura = new LecturaDeLaCompania(db);
        var grilla = await LeerAsync(lectura, cabecera, desde, protector, ct).ConfigureAwait(false);
        var pegados = await VehiculosPegadosAsync(lectura, cabecera.Id, reloj.GetUtcNow(), opciones.Value.UmbralPegada, ct).ConfigureAwait(false);
        var esPegado = pegados.ToHashSet();
        // HU #14563 B1: veredicto vigente y elegibilidad de TODO el lote en consultas por conjunto (sin N+1), para los conteos, las filas
        // y la columna de derivación (DerivacionEndpoints.CompletarGrillaAsync la reutiliza). Los estados de las celdas ya están leídos.
        var elegibilidad = await Derivacion.LecturaElegibilidad.LeerAsync(
            db, cabecera.Id, cabecera.Modo, grilla.IdsVehiculos, ct, estadosLeidos: grilla.EstadosPorVehiculo).ConfigureAwait(false);
        var conteos = new ConteosGrillaDto(
            grilla.Avance.Vehiculos, grilla.Avance.Terminados, grilla.ConError, grilla.ConDeuda,
            elegibilidad.Values.Count(e => e.Veredicto == "BLOQUEADO"), elegibilidad.Values.Count(e => e.Elegible));
        var lote = await LoteAsync(cabecera, ct).ConfigureAwait(false);
        return new GrillaDto(
            grilla.Cabecera.Id, grilla.Cabecera.Nombre, CodigosDeEnum.Codigo(grilla.Cabecera.Estado), grilla.Cabecera.VersionTablero,
            grilla.Desde, grilla.Columnas, grilla.Avance,
            [.. grilla.Vehiculos.Select(v => new FilaVehiculoDto(
                v.Id, v.Orden, v.Identificador, v.TipoIdentificador, v.Placa, v.Vin, v.Terminado, [.. v.Celdas.Select(Dto)],
                v.DocumentoDeclarado, v.Propietarios, esPegado.Contains(v.Id),
                Veredicto: elegibilidad.TryGetValue(v.Id, out var e) ? e.Veredicto : null,
                DictamenAjustado: e?.DictamenAjustado ?? false,
                Version: v.VersionFila))],
            grilla.Estimado, pegados.Count, pegados, conteos, lote)
        {
            Elegibilidad = new ElegibilidadDeLaGrilla(cabecera.TenantId, elegibilidad),
        };
    }

    /// <summary>
    /// Bloque <c>lote</c> (HU #14563 B2): modo, trámite, fecha y express salen de la cabecera ya leída; el nombre del trámite, del
    /// catálogo (con caché; si no responde, el código) y el del creador, de <see cref="IResolutorNombresUsuario"/> (null si no lo da).
    /// </summary>
    private async Task<LoteGrillaDto> LoteAsync(CabeceraDiagnostico cabecera, CancellationToken ct)
    {
        TipoTramiteListadoDto? tipo = null;
        if (cabecera.TipoTramiteCodigo is { } codigo)
        {
            var nombre = codigo;
            try
            {
                nombre = (await catalogo.ListarAsync(ct).ConfigureAwait(false)).FirstOrDefault(t => t.Codigo == codigo)?.Nombre ?? codigo;
            }
            catch (CatalogoTiposTramiteNoDisponibleException)
            {
                // Catálogo caído: el nombre es el código, como en el listado.
            }

            tipo = new TipoTramiteListadoDto(codigo, nombre);
        }

        var resueltos = await nombres.ResolverAsync(cabecera.TenantId, [cabecera.UsuarioCreadorId], ct).ConfigureAwait(false);
        var creador = resueltos.GetValueOrDefault(cabecera.UsuarioCreadorId);
        return new LoteGrillaDto(
            CodigosDeEnum.Codigo(cabecera.Modo), tipo, cabecera.CreadoEn, cabecera.EsExpress,
            new UsuarioCreadorGrillaDto(cabecera.UsuarioCreadorId, creador is null or ResolutorNombresUsuario.NoDisponible ? null : creador));
    }

    /// <summary>
    /// Vehículos del lote con alguna celda que el reintento reencolaría (<see cref="CriterioPegada.Reencolables"/>, Punto 3), en el
    /// orden del lote. Lee solo la RUNT y las celdas no cerradas (ERROR, PENDIENTE, EN_CURSO), sin el payload.
    /// </summary>
    internal static async Task<IReadOnlyList<Guid>> VehiculosPegadosAsync(
        ILecturaTablero lectura, Guid diagnosticoId, DateTimeOffset ahora, TimeSpan umbral, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lectura);
        var celdas = await lectura.Filas<CeldaConsulta>()
            .Where(c => c.DiagnosticoId == diagnosticoId
                && (c.Fuente == Fuente.Runt || c.Estado == EstadoCelda.Error || c.Estado == EstadoCelda.Pendiente || c.Estado == EstadoCelda.EnCurso))
            .Select(c => new CeldaParaReintento(c.Id, c.VehiculoId, c.Fuente, c.Estado, c.ActualizadoEn))
            .ToListAsync(ct).ConfigureAwait(false);
        var pegados = CriterioPegada.Reencolables(celdas, ahora, umbral).Select(c => c.VehiculoId).Distinct().ToList();
        if (pegados.Count == 0)
            return [];
        var orden = await lectura.Filas<VehiculoDiagnostico>()
            .Where(v => v.DiagnosticoId == diagnosticoId && pegados.Contains(v.Id))
            .OrderBy(v => v.Orden).Select(v => v.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return orden;
    }

    /// <summary><c>desde</c> es la versión que ya tiene el cliente: 0 o mayor; si no, 400 <c>DESDE_INVALIDO</c>.</summary>
    public static void ValidarDesde(long desde)
    {
        if (desde < 0)
            throw new ReglaException(CodigoDesdeInvalido, "desde es la versión del tablero que ya tiene el cliente: 0 o mayor.", StatusCodes.Status400BadRequest) { Campo = "desde" };
    }

    /// <summary>
    /// Lee la grilla de <paramref name="cabecera"/> desde la versión dada con <paramref name="lectura"/> (la cabecera ya pasó el
    /// control de acceso de quien llama). Las celdas traen el origen y el usuario manual: cada llamador decide qué proyecta. Los
    /// documentos de las filas que viajan (HU #14087) salen ya enmascarados con <paramref name="protector"/>, leídos en UNA
    /// consulta para todas las filas (sin N+1).
    /// </summary>
    public static async Task<GrillaLeida> LeerAsync(
        ILecturaTablero lectura, CabeceraDiagnostico cabecera, long desde, IProtectorDocumentoPersona protector, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lectura);
        ArgumentNullException.ThrowIfNull(cabecera);
        ArgumentNullException.ThrowIfNull(protector);
        var id = cabecera.Id;
        var celdas = await lectura.Filas<CeldaConsulta>()
            .Where(c => c.DiagnosticoId == id)
            .Select(c => new CeldaLeida(
                c.Id, c.VehiculoId, c.Fuente, c.Estado, c.MotivoNa, c.TipoError, c.DatoObtenidoEn, c.ReutilizadaDeId != null, c.VersionTablero,
                c.Origen, c.UsuarioManualId, c.Payload))
            .ToListAsync(ct).ConfigureAwait(false);
        var porVehiculo = celdas.GroupBy(c => c.VehiculoId).ToDictionary(g => g.Key, g => g.ToList());
        var cambiadas = celdas.Where(c => c.Version > desde).ToList();
        var idsCambiados = cambiadas.Select(c => c.VehiculoId).Distinct().ToList();

        var vehiculos = lectura.Filas<VehiculoDiagnostico>().Where(v => v.DiagnosticoId == id);
        var totalVehiculos = await vehiculos.CountAsync(ct).ConfigureAwait(false);
        // HU #14563 B1 (ADR-0086 D-1): en el delta viajan las filas con celdas cambiadas o con versión de fila mayor que desde.
        var filas = await (desde == 0 ? vehiculos : vehiculos.Where(v => idsCambiados.Contains(v.Id) || v.VersionTablero > desde))
            .OrderBy(v => v.Orden)
            .Select(v => new
            {
                v.Id,
                v.Orden,
                v.IdentificadorNorm,
                v.TipoIdentificador,
                v.Placa,
                v.Vin,
                v.VersionTablero,
                Captura = new DocumentoCapturado(
                    v.LocatarioDocOrigen, v.LocatarioDocTipo, v.LocatarioDocCifrado, v.LocatarioDeclaradoDocTipo, v.LocatarioDeclaradoDocCifrado),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var documentos = await DocumentosAsync(lectura, cabecera.TenantId, [.. filas.Select(v => (v.Id, v.Captura))], protector, ct).ConfigureAwait(false);

        var ordenFuente = cabecera.Alcance.Distinct().Select((f, i) => (f, i)).ToDictionary(x => x.f, x => x.i);
        var cambiadasPorVehiculo = cambiadas.ToLookup(c => c.VehiculoId);
        var filasLeidas = filas.Select(v => new VehiculoLeido(
            v.Id, v.Orden, v.IdentificadorNorm, CodigosDeEnum.Codigo(v.TipoIdentificador), v.Placa, v.Vin,
            AvanceVehiculo.Terminado(porVehiculo.GetValueOrDefault(v.Id, []).Select(c => c.Estado)),
            [.. cambiadasPorVehiculo[v.Id].OrderBy(c => ordenFuente.GetValueOrDefault(c.Fuente, int.MaxValue))],
            documentos[v.Id].Declarado, documentos[v.Id].Propietarios, v.VersionTablero)).ToList();
        var terminados = porVehiculo.Values.Count(c => AvanceVehiculo.Terminado(c.Select(x => x.Estado)));
        // HU #14563 B1: con todo el lote (también en el delta), con la lista de celdas ya leída.
        var conError = porVehiculo.Values.Count(c => c.Any(x => x.Estado == EstadoCelda.Error));
        var conDeuda = porVehiculo.Values.Count(c => c.Any(TieneDeuda));

        return new GrillaLeida(
            cabecera, desde, AlcanceDto.De(Alcance.Agrupar(cabecera.Alcance)), new AvanceLoteDto(totalVehiculos, terminados), filasLeidas,
            await EstimadoAsync(lectura, cabecera, ct).ConfigureAwait(false))
        {
            ConError = conError,
            ConDeuda = conDeuda,
            IdsVehiculos = [.. porVehiculo.Keys],
            EstadosPorVehiculo = celdas.ToLookup(c => c.VehiculoId, c => c.Estado),
        };
    }

    /// <summary>
    /// Documentos de las filas que viajan (HU #14087), en UNA consulta a <c>personas_vehiculo</c>: los PROPIETARIO en su orden y el
    /// declarado por el cliente. El declarado es la persona con origen <c>DECLARADO_CLIENTE</c>; si el vehículo aún no tiene
    /// personas (RUNT sin cerrar) o el proveedor trajo su locatario, el de la captura (<c>locatario_doc_*</c> si sigue siendo el
    /// declarado; si no, su respaldo <c>locatario_declarado_doc_*</c>). Todo se enmascara aquí; el número en claro no sale.
    /// </summary>
    private static async Task<Dictionary<Guid, DocumentosFila>> DocumentosAsync(
        ILecturaTablero lectura, Guid tenant, IReadOnlyList<(Guid Id, DocumentoCapturado Captura)> filas, IProtectorDocumentoPersona protector,
        CancellationToken ct)
    {
        if (filas.Count == 0)
            return [];
        var ids = filas.Select(f => f.Id).ToList();
        var personas = await lectura.Filas<PersonaVehiculo>()
            .Where(p => p.TenantId == tenant && ids.Contains(p.VehiculoId)
                && (p.Rol == RolPersona.Propietario || p.Origen == OrigenDocumento.DeclaradoCliente))
            .OrderBy(p => p.Rol).ThenBy(p => p.Orden)
            .Select(p => new { p.VehiculoId, p.Rol, p.DocTipo, p.DocCifrado, p.Origen })
            .ToListAsync(ct).ConfigureAwait(false);
        var porVehiculo = personas.ToLookup(p => p.VehiculoId);
        return filas.ToDictionary(f => f.Id, f =>
        {
            var propias = porVehiculo[f.Id].ToList();
            var declarada = propias.FirstOrDefault(p => p.Origen == OrigenDocumento.DeclaradoCliente);
            var declarado = declarada is not null
                ? new DocumentoDeclaradoDto(CodigosDeEnum.Codigo(declarada.Rol), declarada.DocTipo, EnmascaradorDocumento.EnmascararCifrado(protector, declarada.DocCifrado))
                : f.Captura.Declarado() is { } capturado
                    ? new DocumentoDeclaradoDto(CodigosDeEnum.Codigo(RolPersona.Locatario), capturado.Tipo, EnmascaradorDocumento.EnmascararCifrado(protector, capturado.Cifrado))
                    : null;
            return new DocumentosFila(declarado, [.. propias
                .Where(p => p.Rol == RolPersona.Propietario)
                .Select(p => new PropietarioGrillaDto(p.DocTipo, EnmascaradorDocumento.EnmascararCifrado(protector, p.DocCifrado)))]);
        });
    }

    private static CeldaGrillaDto Dto(CeldaLeida c) => new(
        c.Id, CodigosDeEnum.Codigo(c.Fuente), CodigosDeEnum.Codigo(c.Estado), c.MotivoNa is { } m ? CodigosDeEnum.Codigo(m) : null,
        c.TipoError, c.DatoObtenidoEn, c.Reutilizada, c.Version, TieneDeuda(c));

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

    /// <summary>El semáforo del impuesto deja de ser verde cuando la celda confirma deuda, con o sin monto.</summary>
    private static bool TieneDeuda(CeldaLeida celda)
    {
        if (celda.Fuente != Fuente.ImpuestoDepartamental || celda.Estado != EstadoCelda.Completado || string.IsNullOrWhiteSpace(celda.Payload))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(celda.Payload);
            return ResultadoImpuestoDetalle.Desde(doc.RootElement)?.TieneDeuda == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>La grilla leída (HU #13799): común a la compañía y al backoffice; cada uno la proyecta a su DTO.</summary>
internal sealed record GrillaLeida(
    CabeceraDiagnostico Cabecera, long Desde, AlcanceDto Columnas, AvanceLoteDto Avance, IReadOnlyList<VehiculoLeido> Vehiculos,
    IReadOnlyList<EstimadoVsConsumidoDto>? Estimado)
{
    /// <summary>Vehículos del lote con alguna celda en ERROR (HU #14563).</summary>
    public int ConError { get; init; }

    /// <summary>Vehículos del lote con deuda de impuesto confirmada, la misma definición del semáforo (HU #14563).</summary>
    public int ConDeuda { get; init; }

    /// <summary>Ids de todos los vehículos del lote con celdas (para la elegibilidad de los conteos, HU #14563).</summary>
    public IReadOnlyList<Guid> IdsVehiculos { get; init; } = [];

    /// <summary>Estados de las celdas de todo el lote, ya leídos: la elegibilidad no los vuelve a leer (HU #14563).</summary>
    public ILookup<Guid, EstadoCelda> EstadosPorVehiculo { get; init; } = Array.Empty<EstadoCelda>().ToLookup(_ => Guid.Empty);
}

/// <summary>
/// Un vehículo de la grilla con sus celdas cambiadas desde <c>desde</c>, en el orden del alcance, y sus documentos ya
/// enmascarados (HU #14087).
/// </summary>
internal sealed record VehiculoLeido(
    Guid Id, int Orden, string Identificador, string TipoIdentificador, string? Placa, string? Vin, bool Terminado, IReadOnlyList<CeldaLeida> Celdas,
    DocumentoDeclaradoDto? DocumentoDeclarado, IReadOnlyList<PropietarioGrillaDto> Propietarios, long VersionFila = 0);

/// <summary>
/// La elegibilidad que ya leyó la grilla de la compañía (HU #14563 B1), con la empresa del lote: la columna de derivación la reutiliza
/// en vez de volver a leerla. Nunca se serializa.
/// </summary>
internal sealed record ElegibilidadDeLaGrilla(Guid TenantId, IReadOnlyDictionary<Guid, Derivacion.ElegibilidadVehiculo> PorVehiculo);

/// <summary>Documentos enmascarados de una fila (HU #14087).</summary>
internal sealed record DocumentosFila(DocumentoDeclaradoDto? Declarado, IReadOnlyList<PropietarioGrillaDto> Propietarios);

/// <summary>
/// El locatario de la captura guardado en el vehículo (columnas <c>locatario_doc_*</c> y su respaldo), cifrado. Solo para resolver el
/// documento declarado de la fila cuando aún no hay personas (HU #14087).
/// </summary>
internal sealed record DocumentoCapturado(
    OrigenDocumento? Origen, string? Tipo, byte[]? Cifrado, string? TipoRespaldo, byte[]? CifradoRespaldo)
{
    /// <summary>El declarado vigente o, si el proveedor lo reemplazó, su respaldo; null si el cliente no declaró ninguno.</summary>
    public (string Tipo, byte[] Cifrado)? Declarado() =>
        Origen == OrigenDocumento.DeclaradoCliente && Tipo is { } tipo && Cifrado is { } cifrado ? (tipo, cifrado)
        : TipoRespaldo is { } tipoRespaldo && CifradoRespaldo is { } cifradoRespaldo ? (tipoRespaldo, cifradoRespaldo)
        : null;
}

/// <summary>
/// Una celda leída, con el origen del dato y el usuario del llenado manual: solo el DTO del backoffice los proyecta; el de la
/// compañía los descarta (R-20).
/// </summary>
internal sealed record CeldaLeida(
    Guid Id, Guid VehiculoId, Fuente Fuente, EstadoCelda Estado, MotivoNa? MotivoNa, string? TipoError, DateTimeOffset? DatoObtenidoEn,
    bool Reutilizada, long Version, OrigenCelda? Origen, Guid? UsuarioManualId, string? Payload = null);
