using Flit.Api.Grpc;
using Flit.Infrastructure.PersonaNatural;
using Flit.Infrastructure.Persistence;
using Flit.Platform.Sdk;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.PersonaNatural;

/// <summary>
/// Resultado de procesar una solicitud radicada: trámite creado (nuevo o ya existente) o rechazo de negocio con su código
/// estable y su motivo en lenguaje claro. <see cref="Ignorada"/>: no hay nada que resolver (la solicitud ya no está en cola
/// o no existe), así que no se publica ningún evento.
/// </summary>
internal sealed record ResultadoRadicacionPortal(
    bool Creada, Guid? ProcedureInstanceId, string? NumeroRadicado, string? Estado, string? Codigo, string? Motivo, bool Ignorada = false)
{
    public static ResultadoRadicacionPortal Nueva(Guid id, string? radicado, string? estado) => new(true, id, radicado, estado, null, null);

    public static ResultadoRadicacionPortal Rechazo(string codigo, string motivo) => new(false, null, null, null, codigo, motivo);

    public static readonly ResultadoRadicacionPortal SinEfecto = new(false, null, null, null, null, null, Ignorada: true);
}

/// <summary>Puerto del consumidor: procesa la solicitud radicada. Las pruebas lo reemplazan por un doble.</summary>
internal interface IPersonaNaturalRadicador
{
    /// <summary>Crea el trámite o devuelve el rechazo de negocio. Una falla técnica es una excepción (el SDK reintenta).</summary>
    Task<ResultadoRadicacionPortal> ProcesarAsync(RadicacionSolicitadaDatos evento, CancellationToken ct);
}

/// <summary>
/// HU #14050 — punto de entrada del consumidor. La creación NO corre en la transacción de la bandeja del SDK: los casos de uso
/// de FLIT 2 que la componen (gates del organismo, ciclo de vida) abren sus propias transacciones por tenant y no pueden
/// anidarse en otra. Corre en un alcance propio (conexión propia, un guardado por paso, como la integración con ICT) y el
/// resultado se encola en la bandeja del consumidor. La consistencia entre ambos sale de la idempotencia: un reintento retoma
/// el borrador ya creado (<c>external_ref</c>) y un rechazo deja el trámite anulado con su código, nunca a medias.
/// </summary>
internal sealed class PersonaNaturalRadicacionHandler(IServiceScopeFactory scopes, IPersonaNaturalPortalClient portal) : IPersonaNaturalRadicador
{
    public async Task<ResultadoRadicacionPortal> ProcesarAsync(RadicacionSolicitadaDatos evento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evento);
        await using var scope = scopes.CreateAsyncScope();
        var creador = ActivatorUtilities.CreateInstance<PersonaNaturalCreador>(scope.ServiceProvider, portal);
        return await creador.ProcesarAsync(evento, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// HU #14050 (Feature #14024, Épica #14021, ADR-0075 decisión 14) — crea el trámite de una solicitud radicada en el portal de
/// persona natural. Adaptador sobre los casos de uso de FLIT 2 (los mismos que usa la integración con ICT): crear el borrador,
/// sembrar campos y organismo, actores, comercial, prenda, adjuntos por referencia, preflight y radicar. NO reimplementa
/// ninguna regla: cualquier error de un caso de uso es un rechazo de negocio (sin datos técnicos) y una excepción es un fallo
/// técnico que el consumidor reintenta.
/// <para>
/// Un rechazo posterior a la creación del borrador lo deja <c>anulado</c> con el código del rechazo en su historial (así no
/// quedan borradores huérfanos y un reintento del mismo evento devuelve el mismo rechazo). Un reintento tras una falla
/// técnica retoma el borrador existente: todos los pasos son idempotentes. La referencia externa
/// (<c>external_ref = solicitudId</c>) es la llave de idempotencia.
/// </para>
/// <para>
/// Decisión del PO: las validaciones de identidad del portal no se reutilizan ni vencen por días. El trámite las REFERENCIA
/// tal cual (liga cada validación de ESTA solicitud al trámite) y nunca busca una identidad vigente por documento.
/// </para>
/// <para>Habeas Data: nada de lo que viaja por el RPC (documento, nombre, correo, teléfono, dirección) se escribe en logs.</para>
/// </summary>
internal sealed class PersonaNaturalCreador(
    FlitDbContext db,
    IPersonaNaturalPortalClient portal,
    PersonaNaturalTiposReader tipos,
    ITransitOfficeResolver oficinas,
    CreateProcedureInstanceHandler crear,
    PatchFieldValuesHandler campos,
    PutActorsHandler actores,
    PutCommercialHandler comercial,
    RegistrarPrendaHandler prenda,
    RegisterIntegrationAttachmentHandler adjuntos,
    RunPreflightHandler preflight,
    SubmitProcedureInstanceHandler radicar,
    TransitionProcedureInstanceHandler transicion,
    ILogger<PersonaNaturalCreador> logger)
{
    private const string OrigenAdjuntos = "portal";

    /// <summary>Prefijo del motivo con el que se anula un borrador de una solicitud rechazada; el código va entre corchetes.</summary>
    internal const string PrefijoAnulacion = "Persona Natural: solicitud rechazada en la creación";

    /// <summary>Roles de parte que el portal entrega y que FLIT 2 sabe guardar (<c>ParteRol</c>).</summary>
    private static readonly HashSet<string> RolesSoportados = new(StringComparer.Ordinal) { "comprador", "vendedor", "locatario" };

    /// <summary>
    /// Requisitos que FLIT 2 resuelve con su propio ciclo (FUR, firma de la compraventa, impronta): no los puede aportar el
    /// ciudadano, así que su ausencia NO rechaza la solicitud; el trámite queda creado en borrador para completarlo.
    /// </summary>
    private static readonly HashSet<string> RequisitosInternos = new(StringComparer.Ordinal)
    {
        SubmitGate.FurRequerido, SubmitGate.FirmaCompraventaRequerida, SubmitGate.ImprontaRequerida,
    };

    public async Task<ResultadoRadicacionPortal> ProcesarAsync(RadicacionSolicitadaDatos evento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evento);
        var empresa = PlatformTenants.PersonaNatural;
        var externalRef = evento.SolicitudId.ToString();

        // Idempotencia por referencia externa. Anulado por rechazo → el mismo rechazo; ya radicado → el mismo trámite;
        // borrador → se retoma (un intento previo se cayó o quedó pendiente de FLIT 2).
        Guid? retomar = null;
        var existente = await db.Set<ProcedureInstance>().AsNoTracking()
            .Where(p => p.TenantId == empresa && p.ExternalRef == externalRef && p.DeletedAt == null)
            .Select(p => new { p.Id, p.ReferenceNumber, p.Status })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existente is not null)
        {
            if (existente.Status == TramiteEstado.Anulado)
                return await RechazoPreviamenteAsync(existente.Id, ct).ConfigureAwait(false);
            if (existente.Status != TramiteEstado.Borrador)
                return ResultadoRadicacionPortal.Nueva(existente.Id, existente.ReferenceNumber, existente.Status);
            retomar = existente.Id;
        }

        // Elegibilidad del tipo y organismo habilitado ANTES de leer datos personales.
        var tipo = (await tipos.ListarAsync(ct, evento.TipoTramiteCodigo).ConfigureAwait(false))
            .FirstOrDefault(t => string.Equals(t.Codigo, evento.TipoTramiteCodigo, StringComparison.OrdinalIgnoreCase));
        if (tipo is null || !tipo.WizardHabilitado || !tipo.Elegibilidad.Elegible)
            return Rechazo(MotivosDeRechazo.TipoNoDisponible);

        var oficina = await oficinas.ResolveEnabledByIdAsync(empresa, evento.OrganismoId, ct).ConfigureAwait(false);
        if (oficina is null)
            return Rechazo(MotivosDeRechazo.OrganismoNoHabilitado);

        var lectura = await portal.ObtenerSolicitudParaRadicarAsync(evento.SolicitudId, empresa, ct).ConfigureAwait(false);
        if (lectura.Solicitud is not { } solicitud)
        {
            // Ya no está en cola o no existe: no hay a quién avisar. No es un error técnico (reintentar no lo arregla).
            RadicacionLog.SolicitudNoLeible(logger, evento.SolicitudId, lectura.Razon ?? "desconocida");
            return ResultadoRadicacionPortal.SinEfecto;
        }

        // Partes: roles que FLIT 2 sabe guardar e identidad aprobada de ESTA solicitud (la compuerta del portal ya lo exigió;
        // se repite porque es lo que el trámite referencia).
        if (solicitud.Partes.Any(p => !RolesSoportados.Contains(p.Rol.Trim().ToLowerInvariant())))
            return Rechazo(MotivosDeRechazo.ParteNoSoportada);
        if (solicitud.Partes.Any(p => p.ValidacionId is null || !string.Equals(p.ValidacionEstado, "aprobada", StringComparison.Ordinal)))
            return Rechazo(MotivosDeRechazo.IdentidadNoValidada);

        var creador = await ResolverCreadorAsync(db, empresa, ct).ConfigureAwait(false);
        var id = retomar;
        if (id is null)
        {
            // El borrador, con origen y referencia externa en el MISMO INSERT.
            var (resumen, error) = await crear.HandleAsync(
                new CreateProcedureInstanceRequest(
                    TenantId: empresa, ProcedureTypeId: null, CreatedByUserId: creador, TransitOfficeId: oficina.Id,
                    ProcedureTypeCode: solicitud.TipoTramiteCodigo, Origin: PersonaNaturalEventos.Origen, ExternalRef: externalRef),
                ct).ConfigureAwait(false);
            if (error is not null || resumen is null)
                return Rechazo(error ?? "crear_fallido");
            id = resumen.Id;
        }

        var resultado = await CompletarAsync(id.Value, solicitud, oficina, creador, ct).ConfigureAwait(false);
        if (!resultado.Creada)
            await AnularAsync(id.Value, creador, resultado.Codigo ?? MotivosDeRechazo.Generico, ct).ConfigureAwait(false);
        return resultado;
    }

    /// <summary>Pasos posteriores a crear el borrador. Todos idempotentes (upsert, dedup por sha256, reglas de FLIT 2).</summary>
    private async Task<ResultadoRadicacionPortal> CompletarAsync(Guid id, SolicitudPortal s, ResolvedTransitOffice oficina, Guid creador, CancellationToken ct)
    {
        var empresa = PlatformTenants.PersonaNatural;

        // 1. Campos del tipo, vehículo y documento del titular (el preflight por placa lo exige, igual que ICT).
        var items = s.Campos.Select(c => new FieldValueInput(null, c.Key, c.Value, null)).ToList();
        if (s.Vin is not null)
            items.Add(new FieldValueInput(null, "vin", s.Vin, null));
        if (s.Placa is not null)
            items.Add(new FieldValueInput(null, "plate", s.Placa, null));
        var titular = s.Partes.FirstOrDefault(p => string.Equals(p.Rol.Trim(), "vendedor", StringComparison.OrdinalIgnoreCase));
        if (titular is not null && !string.IsNullOrWhiteSpace(titular.NumeroDocumento))
        {
            items.Add(new FieldValueInput(null, "owner_document_type", titular.TipoDocumento.Trim().ToUpperInvariant(), null));
            items.Add(new FieldValueInput(null, "owner_document_number", titular.NumeroDocumento.Trim(), null));
        }

        if (items.Count > 0 && await Fallo(campos.HandleAsync(id, empresa, new PatchFieldValuesRequest(items), ct)).ConfigureAwait(false) is { } e1)
            return Rechazo(e1);

        // Organismo en firme, con las mismas claves que el paso 1 del wizard (patch aparte: B11 en traspaso).
        var organismo = new List<FieldValueInput>
        {
            new(null, TransitOfficeFieldKeys.Id, oficina.Id.ToString(), null),
            new(null, TransitOfficeFieldKeys.Code, oficina.Code, null),
        };
        if (!string.IsNullOrWhiteSpace(oficina.Name))
            organismo.Add(new FieldValueInput(null, TransitOfficeFieldKeys.Name, oficina.Name, null));
        if (!string.IsNullOrWhiteSpace(oficina.CityCode))
            organismo.Add(new FieldValueInput(null, TransitOfficeFieldKeys.City, oficina.CityCode, null));
        if (!string.IsNullOrWhiteSpace(oficina.CityName))
            organismo.Add(new FieldValueInput(null, TransitOfficeFieldKeys.CityName, oficina.CityName, null));
        organismo.Add(new FieldValueInput(null, TransitOfficeSelectionPolicy.OrigenFieldKey, TransitOfficeSelectionPolicy.OrigenPasoUno, null));
        db.ChangeTracker.Clear();
        if (await Fallo(campos.HandleSystemSeedAsync(id, empresa, new PatchFieldValuesRequest(organismo), ct)).ConfigureAwait(false) is { } e2)
            return Rechazo(e2);

        // 2. Partes.
        db.ChangeTracker.Clear();
        var inputs = s.Partes.Select(MapearParte).ToList();
        if (inputs.Count > 0)
        {
            var (_, errorActores) = await actores.HandleAsync(id, empresa, new PutActorsRequest(inputs), ct).ConfigureAwait(false);
            if (errorActores is not null)
                return Rechazo(errorActores);
        }

        // 3. Las validaciones de identidad de ESTA solicitud pasan a ser las del trámite (sin buscar por documento).
        db.ChangeTracker.Clear();
        await LigarValidacionesAsync(id, s.SolicitudId.ToString(), ct).ConfigureAwait(false);

        // 4. Bloques opcionales: solo si el portal los llenó (el tipo los pide).
        db.ChangeTracker.Clear();
        if (s.Datos.ValorComercial is { } valor && valor > 0
            && await Fallo(comercial.HandleAsync(id, empresa, new CommercialDto(valor, "COMPRAVENTA", null, null, null), ct)).ConfigureAwait(false) is { } e3)
            return Rechazo(e3);

        if (s.Datos.Prenda is { } p)
        {
            db.ChangeTracker.Clear();
            var decision = p.Tiene ? PrendaDecision.Registrar : PrendaDecision.SinPrenda;
            var (_, errorPrenda) = await prenda.HandleAsync(id, empresa, new RegistrarPrendaInput(decision, p.Acreedor, p.NitAcreedor), creador, ct).ConfigureAwait(false);
            if (errorPrenda is not null)
                return Rechazo(errorPrenda);
        }

        // 5. Adjuntos por referencia (no se re-suben bytes): origen «portal», idempotentes por sha256.
        if (s.Adjuntos.Count > 0)
        {
            db.ChangeTracker.Clear();
            var entradas = s.Adjuntos.Select(a => new RegisterAttachmentInput(a.Tipo, a.NombreArchivo, a.MimeType, a.TamanoBytes, a.Sha256, a.Referencia)).ToList();
            var (_, avisos) = await adjuntos.HandleBatchAsync(id, empresa, entradas, OrigenAdjuntos, creador, ct).ConfigureAwait(false);
            if (avisos.Count > 0)
                return Rechazo("adjunto_invalido");
        }

        // 6. Preflight (RUNT, comparendos, duplicidad…) con las mismas reglas del wizard.
        if (s.Vin is not null || s.Placa is not null)
        {
            db.ChangeTracker.Clear();
            var (snapshot, errorPreflight, _, _) = await preflight.HandleAsync(id, empresa, null, null, ct).ConfigureAwait(false);
            if (errorPreflight is not null)
                return Rechazo(errorPreflight);
            if (snapshot is not null && string.Equals(snapshot.Overall, "red", StringComparison.Ordinal))
            {
                // Un proveedor caído llega como chequeo «error»: es transitorio y se reintenta; solo un «fail» es un requisito que falta.
                if (!snapshot.Checks.Any(c => string.Equals(c.Status, "fail", StringComparison.Ordinal)))
                    throw new InvalidOperationException("El preflight del trámite del portal no pudo completarse (proveedor sin respuesta); se reintenta.");
                return Rechazo(MotivosDeRechazo.RequisitoFaltante);
            }
        }

        // 7. Radicación: desde borrador pasa por los gates de identidad y documentos y entra a la cola del organismo.
        db.ChangeTracker.Clear();
        var (radicado, errorRadicar) = await radicar.HandleAsync(id, empresa, creador, ct).ConfigureAwait(false);
        if (errorRadicar is not null && !RequisitosInternos.Contains(errorRadicar))
            return Rechazo(errorRadicar);

        db.ChangeTracker.Clear();
        var fila = await db.Set<ProcedureInstance>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.ReferenceNumber, x.Status }).FirstAsync(ct).ConfigureAwait(false);
        if (errorRadicar is not null)
            RadicacionLog.QuedaEnBorrador(logger, s.SolicitudId, id, errorRadicar);
        return ResultadoRadicacionPortal.Nueva(id, fila.ReferenceNumber, radicado?.Status ?? fila.Status);
    }

    /// <summary>
    /// Anula el borrador de una solicitud rechazada, con el código en el motivo (lo lee un reintento). Si la anulación misma
    /// falla se avisa en el log y el rechazo se entrega igual: el borrador queda sin radicar y no sale en ninguna bandeja.
    /// </summary>
    private async Task AnularAsync(Guid id, Guid creador, string codigo, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var (_, error, _) = await transicion.HandleAsync(
            id, PlatformTenants.PersonaNatural, TramiteEstado.Anulado, $"{PrefijoAnulacion} [{MotivosDeRechazo.Codigo(codigo)}]", creador, ct: ct).ConfigureAwait(false);
        if (error is not null)
            RadicacionLog.NoSePudoAnular(logger, id, error);
    }

    /// <summary>El rechazo ya dado a esta solicitud (el código está en el motivo de la anulación).</summary>
    private async Task<ResultadoRadicacionPortal> RechazoPreviamenteAsync(Guid id, CancellationToken ct)
    {
        var motivo = await db.Set<ProcedureInstanceStatusHistory>().AsNoTracking()
            .Where(h => h.ProcedureInstanceId == id && h.ToStatus == TramiteEstado.Anulado)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => h.Reason)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Rechazo(CodigoDeAnulacion(motivo));
    }

    /// <summary>Código entre corchetes del motivo de anulación (<see cref="PrefijoAnulacion"/>); otro motivo es el genérico.</summary>
    internal static string CodigoDeAnulacion(string? motivo)
    {
        if (motivo is null || !motivo.StartsWith(PrefijoAnulacion, StringComparison.Ordinal))
            return MotivosDeRechazo.Generico;
        var abre = motivo.LastIndexOf('[');
        var cierra = motivo.LastIndexOf(']');
        return abre >= 0 && cierra > abre ? MotivosDeRechazo.Codigo(motivo[(abre + 1)..cierra]) : MotivosDeRechazo.Generico;
    }

    private static async Task<string?> Fallo<T>(Task<(T? Result, string? Error)> tarea)
    {
        var (_, error) = await tarea.ConfigureAwait(false);
        return error;
    }

    internal static ActorInput MapearParte(ParteSolicitudPortal p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new ActorInput(
            Rol: p.Rol.Trim().ToLowerInvariant(),
            TipoDocumento: p.TipoDocumento.Trim().ToUpperInvariant(),
            NumeroDocumento: p.NumeroDocumento.Trim(),
            NombreCompleto: p.Nombre.Trim(),
            Email: p.Correo.Trim(),
            Telefono: string.IsNullOrWhiteSpace(p.Celular) ? null : p.Celular.Trim(),
            Direccion: string.IsNullOrWhiteSpace(p.Direccion) ? null : p.Direccion.Trim(),
            Ordinal: p.Ordinal < 1 ? 1 : p.Ordinal);
    }

    /// <summary>
    /// Liga al trámite la validación más reciente de cada (rol, documento) de ESTA solicitud. No consulta por documento ni
    /// toca validaciones de FLIT 2: la ancla <c>portal_solicitud_ref</c> la deja fuera de toda búsqueda de reutilización.
    /// </summary>
    private async Task LigarValidacionesAsync(Guid procedureInstanceId, string solicitudRef, CancellationToken ct)
    {
        var filas = await db.ProcedureInstanceBiometricValidations
            .Where(v => v.TenantId == PlatformTenants.PersonaNatural && v.PortalSolicitudRef == solicitudRef)
            .ToListAsync(ct).ConfigureAwait(false);
        var cambios = false;
        foreach (var grupo in filas.GroupBy(v => (v.PortalParte, v.DocumentNumber.Trim().ToUpperInvariant())))
        {
            var v = grupo.OrderByDescending(x => x.CreatedAt).First();
            if (v.ProcedureInstanceId == procedureInstanceId)
                continue;
            v.ProcedureInstanceId = procedureInstanceId;
            if (v.PortalParte is "comprador" or "vendedor")
                v.PartyRole = v.PortalParte;
            cambios = true;
        }

        if (cambios)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Usuario de servicio del portal en la empresa (<c>procedure_instances.created_by_user_id</c> es FK obligatoria a
    /// <c>identity.users</c> y el ciudadano no tiene cuenta). Obtiene-o-crea, idempotente; sin credenciales.
    /// </summary>
    internal static async Task<Guid> ResolverCreadorAsync(FlitDbContext db, Guid tenantId, CancellationToken ct)
    {
        var email = $"persona-natural+{tenantId}@flit.local";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.users (id, email, display_name, status, created_at, home_tenant_id)
            VALUES (uuidv7(), {email}, 'Portal Persona Natural', 'active', now(), {tenantId})
            ON CONFLICT (email) WHERE deleted_at IS NULL DO NOTHING
            """, ct).ConfigureAwait(false);
        return await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM identity.users WHERE email = {email} AND deleted_at IS NULL")
            .FirstAsync(ct).ConfigureAwait(false);
    }

    private static ResultadoRadicacionPortal Rechazo(string codigo) =>
        ResultadoRadicacionPortal.Rechazo(MotivosDeRechazo.Codigo(codigo), MotivosDeRechazo.Motivo(codigo));
}

/// <summary>
/// Códigos estables y motivos en lenguaje claro de los rechazos de negocio. El motivo nunca cita datos del trámite ni de las
/// personas y no revela detalles técnicos: lo ve el ciudadano. Un código que no está en la tabla cae en el motivo genérico.
/// </summary>
internal static class MotivosDeRechazo
{
    public const string TipoNoDisponible = "tipo_no_disponible";
    public const string OrganismoNoHabilitado = "organismo_no_habilitado";
    public const string ParteNoSoportada = "parte_no_soportada";
    public const string IdentidadNoValidada = "identidad_no_validada";
    public const string RequisitoFaltante = "requisito_faltante";
    public const string Generico = "no_fue_posible_crear";

    private static readonly Dictionary<string, string> Motivos = new(StringComparer.Ordinal)
    {
        [TipoNoDisponible] = "El tipo de trámite ya no está disponible para el portal.",
        ["not_found"] = "El tipo de trámite ya no está disponible para el portal.",
        ["not_published"] = "El tipo de trámite ya no está disponible para el portal.",
        ["procedure_type_not_enabled"] = "El tipo de trámite ya no está disponible para el portal.",
        [OrganismoNoHabilitado] = "El organismo de tránsito elegido ya no recibe trámites del portal. Elija otro organismo.",
        [SubmitGate.OrganismoRequerido] = "El organismo de tránsito elegido ya no recibe trámites del portal. Elija otro organismo.",
        [ParteNoSoportada] = "El trámite incluye una parte que el portal todavía no puede radicar.",
        [IdentidadNoValidada] = "Falta validar la identidad de alguna de las partes del trámite.",
        [SubmitGate.IdentidadNoAprobada] = "Falta validar la identidad de alguna de las partes del trámite.",
        [SubmitGate.DocumentosIncompletos] = "Faltan documentos obligatorios del trámite.",
        ["adjunto_invalido"] = "Alguno de los documentos adjuntos no se pudo registrar. Cárguelo de nuevo.",
        [RequisitoFaltante] = "El vehículo o las partes no cumplen alguno de los requisitos del trámite.",
        [InitialProcedureValidationGate.DuplicateActiveProcedure] = "Ya existe un trámite en proceso para este vehículo.",
        [VehicleStatePolicy.ErrorCode] = "El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite.",
        [VehicleBodyTypePolicy.ErrorCode] = "El vehículo no tiene carrocería registrada: no es posible radicar este trámite.",
        ["procedure_family_blocked"] = "Este tipo de trámite no está disponible por ahora para el organismo elegido.",
    };

    public static string Motivo(string codigo) =>
        Motivos.TryGetValue(codigo, out var m) ? m : "No fue posible crear el trámite con la información enviada. Revise los datos e inténtelo de nuevo.";

    /// <summary>
    /// Código estable y sin datos: los de los casos de uso son constantes de FLIT 2; por defensa se recorta y se limita a
    /// minúsculas, dígitos y <c>_ . : -</c> (jamás un texto libre).
    /// </summary>
    public static string Codigo(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return Generico;
        var limpio = new string([.. codigo.Trim().ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-').Take(80)]);
        return limpio.Length == 0 ? Generico : limpio;
    }
}

internal static partial class RadicacionLog
{
    [LoggerMessage(EventId = 14050, Level = LogLevel.Information, Message = "Portal Persona Natural: la solicitud {SolicitudId} no se puede leer para radicar ({Razon}); no hay nada que crear.")]
    public static partial void SolicitudNoLeible(ILogger logger, Guid solicitudId, string razon);

    [LoggerMessage(EventId = 14051, Level = LogLevel.Warning, Message = "Portal Persona Natural: la solicitud {SolicitudId} creó el trámite {ProcedureInstanceId} pero queda en borrador ({Requisito}): lo completa FLIT 2.")]
    public static partial void QuedaEnBorrador(ILogger logger, Guid solicitudId, Guid procedureInstanceId, string requisito);

    [LoggerMessage(EventId = 14052, Level = LogLevel.Information, Message = "Portal Persona Natural: la solicitud {SolicitudId} resuelta como {Resultado} ({Codigo}).")]
    public static partial void Resuelta(ILogger logger, Guid solicitudId, string resultado, string? codigo);

    [LoggerMessage(EventId = 14054, Level = LogLevel.Warning, Message = "Portal Persona Natural: no se pudo anular el borrador {ProcedureInstanceId} de una solicitud rechazada ({Error}).")]
    public static partial void NoSePudoAnular(ILogger logger, Guid procedureInstanceId, string error);
}
