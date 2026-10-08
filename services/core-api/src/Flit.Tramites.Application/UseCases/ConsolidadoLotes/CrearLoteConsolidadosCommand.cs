using System.Net;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13373, ADR-0070) — pedir la descarga masiva: crea el lote con la selección congelada y su
/// auditoría en una sola transacción. El origen, la compañía, el organismo y el rol los pone el endpoint desde el
/// token / middleware (ajuste 2026-10-07), NUNCA el cuerpo.
/// </summary>
/// <remarks>
/// Uso de ejemplo (#13374):
/// <code>
/// var r = await handler.HandleAsync(new CrearLoteConsolidadosCommand
/// {
///     Origen = ConsolidadoExportOrigin.Tramites, TenantId = tenantDelToken, UsuarioId = sub, RolCodigo = rol,
///     TipoDocumento = body.TipoDocumento, Seleccion = seleccion, ConfirmaEfectos = body.ConfirmaEfectos,
/// }, ct);
/// return r.Creado ? Results.Accepted(..., r.Lote) : MapError(r.Error);
/// </code>
/// </remarks>
public sealed record CrearLoteConsolidadosCommand
{
    /// <summary>Uno de <see cref="ConsolidadoExportOrigin"/>; lo decide el endpoint (A4.1), no el cuerpo.</summary>
    public required string Origen { get; init; }

    /// <summary>
    /// <c>tramites</c>: compañía del token. <c>ot_bandeja</c>: tenant del OT. <c>superadmin</c>: SIEMPRE <c>null</c>.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>Solo <c>superadmin</c>: compañía de <c>X-Tenant-Id</c> que acota la selección (<c>null</c> = todas).</summary>
    public Guid? ScopeTenantId { get; init; }

    /// <summary>Solo y siempre <c>ot_bandeja</c>: organismo del lote (<c>ot_transit_office_id</c>).</summary>
    public Guid? OtTransitOfficeId { get; init; }

    /// <summary>
    /// HU #13417 (ADR-0070 adenda v7) — solo origen <c>tramites</c>: lote desde la vista de red de la cabeza
    /// <see cref="TenantId"/>. Lo arma el endpoint con <see cref="LoteAlcanceRedPolicy.EvaluarAsync"/> desde el
    /// <c>TenantScope</c> del middleware (nunca del cuerpo). <c>null</c> = el lote propio de siempre.
    /// </summary>
    public LoteAlcanceRed? AlcanceRed { get; init; }

    /// <summary><c>sub</c> del token: dueño del lote.</summary>
    public required Guid UsuarioId { get; init; }

    /// <summary>Rol con el que se pide (auditoría y revalidación CF-16).</summary>
    public required string RolCodigo { get; init; }

    /// <summary>Uno de <see cref="ConsolidadoExportDocumentType"/>. <c>null</c> = el del origen.</summary>
    public string? TipoDocumento { get; init; }

    public LoteSeleccion? Seleccion { get; init; }

    /// <summary>CF-08: el usuario aceptó el texto de efectos. Distinto de <c>true</c> → <c>confirmacion_requerida</c>.</summary>
    public bool? ConfirmaEfectos { get; init; }

    public IPAddress? ClientIp { get; init; }

    public string? UserAgent { get; init; }
}

/// <summary>
/// Códigos estables del resultado (los mapea #13374 a HTTP): 400 <see cref="ConfirmacionRequerida"/>,
/// <see cref="TipoNoPermitido"/>, <see cref="SeleccionRequerida"/>; 403 <see cref="SinCompania"/>; 409
/// <see cref="LoteActivo"/>; 422 <see cref="LoteSeleccionInvalidaException.CodigoExcedeTope"/> y
/// <see cref="LoteSeleccionInvalidaException.CodigoFiltroInvalido"/>; 503 <see cref="MotorInactivo"/> y
/// <see cref="LoteNoCreado"/>.
/// </summary>
public static class CrearLoteConsolidadosErrores
{
    public const string ConfirmacionRequerida = "confirmacion_requerida";
    public const string TipoNoPermitido = "tipo_no_permitido";
    public const string SeleccionRequerida = "seleccion_requerida";
    public const string SinCompania = "sin_compania";
    public const string LoteActivo = "lote_activo";
    public const string MotorInactivo = "motor_inactivo";
    public const string LoteNoCreado = "lote_no_creado";
}

/// <param name="Lote">Lote creado (<c>en_cola</c>, total congelado); <c>null</c> si hubo error.</param>
/// <param name="Error">Uno de <see cref="CrearLoteConsolidadosErrores"/> o de <see cref="LoteSeleccionInvalidaException"/>.</param>
/// <param name="Mensaje">Texto para el usuario, sin datos personales.</param>
/// <param name="LoteActivoId">Solo con <see cref="CrearLoteConsolidadosErrores.LoteActivo"/>.</param>
/// <param name="Total">
/// M1 — solo con <see cref="LoteSeleccionInvalidaException.CodigoExcedeTope"/> por tope total: trámites de la
/// selección resuelta. <c>null</c> en el tope de las listas del cuerpo (allí no se resolvió nada).
/// </param>
/// <param name="Tope">M1 — <c>max_items_per_batch</c> vigente, junto a <paramref name="Total"/>.</param>
public sealed record CrearLoteConsolidadosResultado(
    ConsolidadoExportBatch? Lote,
    string? Error = null,
    string? Mensaje = null,
    Guid? LoteActivoId = null,
    int? Total = null,
    int? Tope = null)
{
    public bool Creado => Lote is not null && Error is null;

    internal static CrearLoteConsolidadosResultado Falla(string error, string mensaje, Guid? loteActivoId = null) =>
        new(null, error, mensaje, loteActivoId);
}

/// <summary>Caso de uso de <see cref="CrearLoteConsolidadosCommand"/>. Ver <see cref="IConsolidadoLoteRepository"/>.</summary>
public sealed partial class CrearLoteConsolidadosHandler(
    IConsolidadoLoteRepository repositorio,
    LoteSeleccionResolverPorOrigen resolvers,
    IConsolidadoLoteCipher cipher,
    TimeProvider? reloj = null,
    ILogger<CrearLoteConsolidadosHandler>? logger = null,
    IProcedureTypeRepository? tiposDeTramite = null)
{
    /// <summary>Mensaje de la UI para los 503 (AC8): el usuario no ve el detalle técnico.</summary>
    public const string MensajeNoDisponible = "No se pudo completar la descarga, intente de nuevo";

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<CrearLoteConsolidadosHandler>.Instance;

    public async Task<CrearLoteConsolidadosResultado> HandleAsync(
        CrearLoteConsolidadosCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidarContrato(command);

        // 1. Validaciones del cuerpo (400). Nada toca la base todavía.
        if (command.ConfirmaEfectos != true)
            return CrearLoteConsolidadosResultado.Falla(
                CrearLoteConsolidadosErrores.ConfirmacionRequerida,
                "Debe confirmar los efectos de la descarga masiva antes de crearla.");

        var tipo = command.TipoDocumento ?? TipoPorDefecto(command.Origen);
        if (!TipoPermitido(command.Origen, tipo))
            return CrearLoteConsolidadosResultado.Falla(
                CrearLoteConsolidadosErrores.TipoNoPermitido,
                $"El tipo de documento '{Truncar(tipo)}' no está permitido en este origen.");

        if (command.Seleccion is not { } seleccion)
            return CrearLoteConsolidadosResultado.Falla(
                CrearLoteConsolidadosErrores.SeleccionRequerida, "La solicitud no trae la selección de trámites.");

        // 2. Guard que falla cerrado (A4.1): fuera del Super Admin no hay lote sin compañía.
        if (command.Origen != ConsolidadoExportOrigin.Superadmin && (command.TenantId is null || command.TenantId == Guid.Empty))
            return CrearLoteConsolidadosResultado.Falla(
                CrearLoteConsolidadosErrores.SinCompania, "El usuario no tiene una compañía activa.");

        // 3. Topes Q7 de las listas del cuerpo (422): los aplica la creación para todos los orígenes (el resolver OT no
        // los aplica). El tope TOTAL del lote (M1) va en el paso 6b, sobre la selección resuelta.
        if (ExcedeTope(seleccion) is { } tope)
            return CrearLoteConsolidadosResultado.Falla(LoteSeleccionInvalidaException.CodigoExcedeTope, tope);

        // 4. Motor apagado (AC8, S4 = b): sin lote, sin ítems, sin auditoría y sin purgar el anterior. Sin fila = apagado.
        var settings = await repositorio.ObtenerSettingsAsync(ct).ConfigureAwait(false);
        if (settings is not { IsActive: true })
        {
            LogMotorInactivo(_logger, command.UsuarioId);
            return CrearLoteConsolidadosResultado.Falla(CrearLoteConsolidadosErrores.MotorInactivo, MensajeNoDisponible);
        }

        // 5. CF-17: un lote activo por usuario (camino rápido; la carrera la cierra el índice único parcial).
        if (await repositorio.ObtenerLoteActivoIdAsync(command.UsuarioId, ct).ConfigureAwait(false) is { } activo)
            return LoteActivo(activo);

        // 6. Resolver la selección con la visibilidad del origen; tenant y usuario del token (CF-15).
        // Code review #13216 (Obs2): la lectura se corta en tope + 1 (LIMIT en SQL) para no cargar en memoria una
        // selección que solo puede acabar en 422 (p. ej. el Super Admin con «todas las compañías»).
        var topeTotal = settings.MaxItemsPerBatch;
        var limite = topeTotal + 1;
        var resolver = resolvers.Para(command.Origen);
        var contexto = new LoteSeleccionContexto(
            command.Origen == ConsolidadoExportOrigin.Superadmin ? command.ScopeTenantId : command.TenantId,
            command.UsuarioId,
            command.OtTransitOfficeId,
            command.AlcanceRed?.Alcance);
        List<ProcedureInstanceRef> items;
        int? total = null;
        try
        {
            var refs = await resolver.ResolverAsync(seleccion, contexto, limite, ct).ConfigureAwait(false);
            items = Congelar(command, refs);
            var completa = refs.Count < limite;

            // Con el corte alcanzado, la intersección CF-15 o la deduplicación pudieron dejar el prefijo leído en el tope
            // o por debajo aunque fuera de él haya trámites que sí entran: solo entonces se resuelve sin límite, para que
            // el corte nunca cambie el resultado de una selección que no excede. Camino raro: el repositorio ya acota
            // por la misma compañía que la intersección y devuelve ids únicos.
            if (!completa && items.Count <= topeTotal)
            {
                refs = await resolver.ResolverAsync(seleccion, contexto, limite: null, ct).ConfigureAwait(false);
                items = Congelar(command, refs);
                completa = true;
            }

            // 6b. M1 — tope total (max_items_per_batch), todos los orígenes y modos, sobre la selección YA resuelta:
            // después de las exclusiones (resolver) y de la intersección de seguridad y la deduplicación (Congelar). Con
            // el corte, el total exacto del 422 sale de un COUNT con el mismo predicado (opción A del review: el
            // contrato de `total` no cambia); nunca menor que lo ya leído, por si la base cambió entre las dos consultas.
            if (items.Count > topeTotal)
                total = completa
                    ? items.Count
                    : Math.Max(items.Count, await resolver.ContarAsync(seleccion, contexto, ct).ConfigureAwait(false));
        }
        catch (LoteSeleccionInvalidaException ex)
        {
            return CrearLoteConsolidadosResultado.Falla(ex.Codigo, ex.Message);
        }

        // Se corta antes de cualquier escritura: ni lote, ni ítems, ni lote_creado, ni purga del retenido.
        if (total is { } excedido)
        {
            LogExcedeTope(_logger, command.UsuarioId, command.Origen, excedido, topeTotal);
            return new CrearLoteConsolidadosResultado(
                null,
                LoteSeleccionInvalidaException.CodigoExcedeTope,
                $"La selección tiene {excedido} trámites y el máximo por descarga masiva es {topeTotal}. " +
                "Acota el filtro o desmarca trámites.",
                Total: excedido,
                Tope: topeTotal);
        }

        var tiposConocidos = await TiposDeTramiteSiHacenFaltaAsync(seleccion, ct).ConfigureAwait(false);

        // 7. Una sola transacción: purga del retenido + lote + ítems + auditoría lote_creado.
        var nuevo = new NuevoLoteConsolidados
        {
            TenantId = command.Origen == ConsolidadoExportOrigin.Superadmin ? null : command.TenantId,
            UsuarioId = command.UsuarioId,
            RolCodigo = command.RolCodigo,
            // HU #13417: en un lote de red, scope_tenant_id es la hija acotada (NULL = toda la red).
            ScopeTenantId = command.Origen == ConsolidadoExportOrigin.Superadmin ? command.ScopeTenantId : command.AlcanceRed?.HijaId,
            NetworkScope = command.AlcanceRed is not null,
            Origen = command.Origen,
            TipoDocumento = tipo,
            ModoSeleccion = seleccion is SeleccionPorIds
                ? ConsolidadoExportSelectionMode.Ids
                : ConsolidadoExportSelectionMode.Filtro,
            OtTransitOfficeId = command.Origen == ConsolidadoExportOrigin.OtBandeja ? command.OtTransitOfficeId : null,
            DekEnvuelta = cipher.GenerarDekEnvuelta(),
            EfectosAceptadosEn = _reloj.GetUtcNow(),
            Items = items,
            ResumenFiltroJson = ConsolidadoLoteAuditoria.ResumirSeleccion(
                seleccion,
                command.Origen == ConsolidadoExportOrigin.OtBandeja ? command.OtTransitOfficeId : null,
                tiposConocidos,
                command.AlcanceRed?.Resumen),
            IdsCount = ConsolidadoLoteAuditoria.ContarIds(seleccion),
            ExcluidosCount = ConsolidadoLoteAuditoria.ContarExcluidos(seleccion),
            ClientIp = command.ClientIp,
            UserAgent = command.UserAgent,
        };

        var resultado = await repositorio.CrearAsync(nuevo, ct).ConfigureAwait(false);
        switch (resultado.Estado)
        {
            case CrearLoteEstado.Creado when resultado.Lote is not null:
                LogCreado(_logger, resultado.Lote.Id, command.Origen, items.Count, resultado.LotesPurgados);
                return new CrearLoteConsolidadosResultado(resultado.Lote);
            case CrearLoteEstado.LoteActivo when resultado.LoteActivoId is { } id:
                return LoteActivo(id);
            default:
                LogNoCreado(_logger, command.UsuarioId);
                return CrearLoteConsolidadosResultado.Falla(CrearLoteConsolidadosErrores.LoteNoCreado, MensajeNoDisponible);
        }
    }

    /// <summary>
    /// L3 (Habeas Data): el código de tipo de trámite solo se audita literal si pertenece al catálogo
    /// <c>tramites.procedure_types</c>, que vive en la BD. Se consulta solo si el filtro trae <c>tipoCodigo</c>; sin
    /// repositorio el resumen lo minimiza (falla en privado).
    /// </summary>
    private async Task<IReadOnlySet<string>?> TiposDeTramiteSiHacenFaltaAsync(LoteSeleccion seleccion, CancellationToken ct)
    {
        if (tiposDeTramite is null
            || seleccion is not SeleccionPorFiltro { Filtro: TramitesLoteFiltro { Criterios.TipoCodigo: { } tipo } }
            || string.IsNullOrWhiteSpace(tipo))
            return null;

        var tipos = await tiposDeTramite.ListAsync(null, null, ct).ConfigureAwait(false);
        return tipos.Select(t => t.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Defensa en profundidad de CF-15 sobre lo que devolvió el resolver: en <c>tramites</c> solo la compañía del
    /// token; en <c>superadmin</c> acotado, solo la del scope. Sin duplicados (un trámite por lote), en su orden.
    /// HU #13417 (AC5): en un lote de red, solo las compañías de <see cref="LoteAlcanceRed.Alcance"/>
    /// (<c>ReadTenantIds</c>): la segunda barrera contra ids inyectados, después del <c>WhereTenantInScope</c> en SQL.
    /// </summary>
    private static List<ProcedureInstanceRef> Congelar(CrearLoteConsolidadosCommand command, IReadOnlyList<ProcedureInstanceRef> refs)
    {
        Guid? soloTenant = command.Origen switch
        {
            ConsolidadoExportOrigin.Tramites when command.AlcanceRed is null => command.TenantId,
            ConsolidadoExportOrigin.Superadmin => command.ScopeTenantId,
            _ => null,
        };
        var alcanceRed = command.AlcanceRed?.Alcance.ReadTenantIds;

        var vistos = new HashSet<Guid>();
        var items = new List<ProcedureInstanceRef>(refs.Count);
        foreach (var r in refs)
        {
            if (soloTenant is { } tenant && r.TenantId != tenant)
                continue;
            if (alcanceRed is not null && !alcanceRed.Contains(r.TenantId))
                continue;
            if (vistos.Add(r.Id))
                items.Add(r);
        }

        return items;
    }

    private static CrearLoteConsolidadosResultado LoteActivo(Guid id) =>
        CrearLoteConsolidadosResultado.Falla(
            CrearLoteConsolidadosErrores.LoteActivo, "Ya tiene una descarga masiva en curso.", id);

    private static string? ExcedeTope(LoteSeleccion seleccion) => seleccion switch
    {
        SeleccionPorIds { Ids.Count: > LoteSeleccionTopes.MaxIds } porIds =>
            $"La selección trae {porIds.Ids.Count} trámites y el máximo es {LoteSeleccionTopes.MaxIds}. " +
            "Para descargar más, usa «Seleccionar todos» con un filtro.",
        SeleccionPorFiltro { Excluidos.Count: > LoteSeleccionTopes.MaxExcluidos } porFiltro =>
            $"La selección excluye {porFiltro.Excluidos!.Count} trámites y el máximo es {LoteSeleccionTopes.MaxExcluidos}. " +
            "Acota el filtro en lugar de desmarcar tantos.",
        _ => null,
    };

    /// <summary>FA1 <c>tramites</c>: solo consolidado (Q-1). Super Admin: ambos (A4.2). Bandeja OT: solo maestro (A5).</summary>
    public static bool TipoPermitido(string origen, string tipo) => origen switch
    {
        ConsolidadoExportOrigin.Tramites => tipo == ConsolidadoExportDocumentType.Consolidado,
        ConsolidadoExportOrigin.Superadmin => ConsolidadoExportDocumentType.Todos.Contains(tipo),
        ConsolidadoExportOrigin.OtBandeja => tipo == ConsolidadoExportDocumentType.ConsolidadoMaestro,
        _ => false,
    };

    private static string TipoPorDefecto(string origen) =>
        origen == ConsolidadoExportOrigin.OtBandeja
            ? ConsolidadoExportDocumentType.ConsolidadoMaestro
            : ConsolidadoExportDocumentType.Consolidado;

    /// <summary>
    /// Errores de composición del LLAMADOR (el endpoint), no del usuario: origen desconocido, Super Admin con
    /// compañía, organismo fuera de <c>ot_bandeja</c> o ausente en él, sin usuario o sin rol.
    /// </summary>
    private static void ValidarContrato(CrearLoteConsolidadosCommand c)
    {
        if (!ConsolidadoExportOrigin.Todos.Contains(c.Origen))
            throw new ArgumentException($"Origen de lote desconocido: '{c.Origen}'.", nameof(c));
        if (c.UsuarioId == Guid.Empty)
            throw new ArgumentException("El lote exige el usuario del token.", nameof(c));
        if (string.IsNullOrWhiteSpace(c.RolCodigo))
            throw new ArgumentException("El lote exige el rol del solicitante.", nameof(c));
        if (c.Origen == ConsolidadoExportOrigin.Superadmin && c.TenantId is not null)
            throw new ArgumentException("Un lote de Super Admin no lleva compañía (Q8): use ScopeTenantId.", nameof(c));
        if (c.Origen != ConsolidadoExportOrigin.Superadmin && c.ScopeTenantId is not null)
            throw new ArgumentException("ScopeTenantId solo aplica al origen superadmin.", nameof(c));
        if ((c.Origen == ConsolidadoExportOrigin.OtBandeja) != (c.OtTransitOfficeId is { } o && o != Guid.Empty))
            throw new ArgumentException("El organismo es obligatorio en ot_bandeja y exclusivo de ese origen (R-d).", nameof(c));
        if (c.AlcanceRed is { } red)
        {
            // HU #13417: la vista de red es de una cabeza de compañía (origen tramites), y su alcance es el de esa cabeza.
            if (c.Origen != ConsolidadoExportOrigin.Tramites)
                throw new ArgumentException("El alcance de red solo aplica al origen tramites.", nameof(c));
            var cabeza = red.HijaId is null ? red.Alcance.WriteTenantId : null;
            if ((cabeza is not null && cabeza != c.TenantId) || red.HijaId == c.TenantId)
                throw new ArgumentException("El alcance de red no es el de la compañía del lote.", nameof(c));
        }
    }

    private static string Truncar(string v) => v.Length <= 40 ? v : v[..40];

    // Logs sin PII: solo ids, origen y conteos.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Lote de consolidados rechazado: motor inactivo. Usuario {UsuarioId}.")]
    private static partial void LogMotorInactivo(ILogger logger, Guid usuarioId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote de consolidados {LoteId} creado: origen {Origen}, {Total} ítems, {Purgados} lote(s) retenido(s) purgado(s).")]
    private static partial void LogCreado(ILogger logger, Guid loteId, string origen, int total, int purgados);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote de consolidados rechazado por tope total: usuario {UsuarioId}, origen {Origen}, {Total} trámites, tope {Tope}.")]
    private static partial void LogExcedeTope(ILogger logger, Guid usuarioId, string origen, int total, int tope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lote de consolidados no creado para el usuario {UsuarioId}: la transacción falló.")]
    private static partial void LogNoCreado(ILogger logger, Guid usuarioId);
}
