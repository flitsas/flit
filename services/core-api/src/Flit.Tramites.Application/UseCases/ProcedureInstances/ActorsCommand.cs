using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Contrato congelado consumido por el frontend (Slice 2).
/// <c>rol</c>: comprador|vendedor; <c>tipoDocumento</c>: CC|CE|NIT|PAS|TI.
/// <c>ciudad</c>/<c>direccion</c> son opcionales y se persisten en <c>actor.metadata</c> (JSON).
/// <c>representanteLegal</c>: solo aplica a persona jurídica (NIT); es una persona natural
/// capturada/consultada en el RUNT y se persiste embebida en <c>actor.metadata</c> (sin DDL).
/// </summary>
/// <remarks>
/// HU #10878 (Feature #10862, CF-04, ADR-0031): <see cref="AutorizaReutilizacionDatos"/> es la
/// captura ampliada (aditiva) del consentimiento Habeas Data para reúso cross-trámite de los datos
/// de ESTA parte. Default <c>false</c> — no-op (no crea ni degrada ningún consentimiento previo);
/// solo <c>true</c> hace upsert a <c>granted</c> en <see cref="Flit.Tramites.Domain.Entities.PersonDataConsent"/>
/// (fail-safe, ver <see cref="PutActorsHandler"/>). La captura UI (checkbox) llega en la HU de
/// frontend #10885 — este campo ya queda disponible en el contrato para quien la implemente.
/// </remarks>
public sealed record ActorInput(
    string Rol,
    string TipoDocumento,
    string NumeroDocumento,
    string NombreCompleto,
    string Email,
    string? Telefono,
    string? Ciudad = null,
    string? Direccion = null,
    string? PersonType = null,
    bool EsRepresentanteLegal = false,
    ActorRepresentanteLegal? RepresentanteLegal = null,
    bool AutorizaReutilizacionDatos = false);

public sealed record ActorDto(
    string Rol,
    string TipoDocumento,
    string NumeroDocumento,
    string NombreCompleto,
    string Email,
    string? Telefono,
    string? Ciudad = null,
    string? Direccion = null,
    string? PersonType = null,
    bool EsRepresentanteLegal = false,
    ActorRepresentanteLegal? RepresentanteLegal = null);

/// <summary>
/// Representante legal / apoderado de una persona jurídica (persona natural). Datos capturados
/// manualmente o autopoblados desde el RUNT. Todos opcionales: es información de contacto, no un
/// actor de primera clase (no participa en biométrica ni en los gates del wizard).
/// </summary>
public sealed record ActorRepresentanteLegal(
    string? TipoDocumento,
    string? NumeroDocumento,
    string? NombreCompleto,
    string? Email,
    string? Telefono);

public sealed record PutActorsRequest(IReadOnlyList<ActorInput> Actors);

public sealed record ActorsResponse(IReadOnlyList<ActorDto> Actors);

/// <summary>
/// Captura de actores end-to-end. El rol vive en <c>actor_type</c>; cada rol se mapea a
/// una <c>procedure_entity</c> seedeada (HU10151): comprador→BUYER, vendedor→OWNER. Esto
/// respeta el UNIQUE(procedure_instance_id, procedure_entity_id): cada rol aparece a lo sumo
/// una vez por instancia. Los roles permitidos salen de la matriz de dominio según
/// <c>modalidad_entrada</c> (matricula_inicial→comprador; traspaso→vendedor+comprador).
/// </summary>
/// <remarks>
/// HU #10880: si el correo del SUJETO DE IDENTIDAD de una parte (el actor en persona natural; el
/// representante legal en persona jurídica — <see cref="IdentitySubjectResolver"/>) cambia respecto al
/// valor persistido, la validación de identidad previa (si ya fue enviada) queda <c>expirado</c> y se
/// reenvía automáticamente al nuevo correo reutilizando <see cref="IniciarKyverumVerifyHandler"/> (mismo
/// mecanismo que emite el <c>CaptureUrl</c> real). Solo aplica cuando el proveedor configurado es Kyverum
/// (<see cref="BiometricsProviderOptions.IsKyverum"/>): el proveedor mock no emite CaptureUrl ni envía
/// correos, así que solo se expira la previa. Si el correo no cambia (comparado normalizado: trim +
/// minúsculas) no se toca la biométrica (AC2).
/// </remarks>
public sealed class PutActorsHandler(
    IProcedureInstanceRepository repo,
    ICatalogRepository catalogRepo,
    BiometricsProviderOptions providerOptions,
    IniciarKyverumVerifyHandler kyverumHandler,
    IPersonDataConsentRepository consentRepo)
{
    // Documentos válidos del contrato congelado (front consume el mismo set).
    private static readonly HashSet<string> ValidDocumentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "CC", "CE", "NIT", "PAS", "TI" };

    // Charset del número de documento (espejo de fieldRules.ts): pasaporte admite letras y
    // números; el resto (CC/CE/TI/NIT) solo dígitos.
    private static readonly Regex DocNumeric = new("^[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex DocAlphanumeric = new("^[A-Za-z0-9]+$", RegexOptions.Compiled);

    // rol (actor_type) → code de procedure_entities (catálogo HU10151).
    private static readonly Dictionary<ParteRol, string> RolToEntityCode =
        new()
        {
            [ParteRol.Comprador] = "BUYER",
            [ParteRol.Vendedor] = "OWNER",
        };

    public async Task<(ActorsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        PutActorsRequest request,
        CancellationToken ct = default)
    {
        // HU #10880: se necesitan también las validaciones biométricas para poder invalidar/reenviar la
        // previa cuando el correo del sujeto de identidad cambie (mismo repo que EnsureIdentityHandler /
        // IniciarKyverumVerifyHandler).
        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        // HU #10870 (AC1) — subsanación reabre la edición COMPLETA del trámite (entregado/rechazado
        // → subsanacion) SIN pasar por borrador: los actores se editan igual que en borrador. Mismo
        // criterio que PatchFieldValuesHandler y el trigger de BD (trg_field_value_immutable). Fuera
        // de borrador/subsanacion, el PUT de actores sigue bloqueado.
        if (instance.Status != TramiteEstado.Borrador && instance.Status != TramiteEstado.Subsanacion)
            return (null, "not_draft");

        var inputs = request.Actors ?? [];

        // 1. Validación de forma de cada actor (documento/email/rol conocidos).
        foreach (var a in inputs)
        {
            var rol = ParseRol(a.Rol);
            if (rol is null)
                return (null, "invalid_rol");
            if (string.IsNullOrWhiteSpace(a.TipoDocumento) || !ValidDocumentTypes.Contains(a.TipoDocumento))
                return (null, "invalid_document_type");
            if (string.IsNullOrWhiteSpace(a.NumeroDocumento))
                return (null, "missing_document_number");
            var docNumber = a.NumeroDocumento.Trim();
            var isPassport = string.Equals(a.TipoDocumento.Trim(), "PAS", StringComparison.OrdinalIgnoreCase);
            var docValid = isPassport ? DocAlphanumeric.IsMatch(docNumber) : DocNumeric.IsMatch(docNumber);
            if (!docValid)
                return (null, "invalid_document_number");
            if (string.IsNullOrWhiteSpace(a.NombreCompleto))
                return (null, "missing_full_name");
            if (string.IsNullOrWhiteSpace(a.Email) || !TramiteDocumento.EmailValido(a.Email))
                return (null, "invalid_email");
            // HU #10542: tipo de persona opcional; si viene, debe ser natural|juridical.
            if (!string.IsNullOrWhiteSpace(a.PersonType) && !ActorPersonTypes.IsValid(a.PersonType))
                return (null, "invalid_person_type");
            // HU #10688 (Fase 1): en persona jurídica el correo del representante legal es
            // obligatorio (es quien valida la identidad de la PJ y recibe el correo de validación).
            // Nombre/documento del RL siguen opcionales. No aplica a persona natural.
            if (ActorPersonTypes.IsJuridical(a.PersonType))
            {
                var rlEmail = a.RepresentanteLegal?.Email;
                if (string.IsNullOrWhiteSpace(rlEmail) || !TramiteDocumento.EmailValido(rlEmail))
                    return (null, "rl_email_requerido");
            }
        }

        // 2. Roles permitidos según modalidad_entrada (matriz de dominio, no hardcode).
        var journey = ResolveJourney(instance);
        var allowedRoles = journey is null
            ? new HashSet<ParteRol>()
            : journey.Partes.Select(p => p.Rol).ToHashSet();

        var providedRoles = new List<ParteRol>();
        foreach (var a in inputs)
        {
            var rol = ParseRol(a.Rol)!.Value;
            if (!allowedRoles.Contains(rol))
                return (null, "rol_not_allowed");
            if (providedRoles.Contains(rol))
                return (null, "duplicate_rol"); // un actor por rol (1:1 con la entity).
            providedRoles.Add(rol);
        }

        // 3. PUT incremental (upsert por rol): el wizard guarda un rol por paso
        // (vendedor en el paso 3, comprador en el paso 4). NO se exige aquí la
        // completitud de roles obligatorios — eso lo validan los gates de pasos 3–4
        // y el SubmitGate al finalizar. Por eso ya no hay check de "missing_required_rol".

        // 4. Unicidad vendedor ≠ comprador (documento y email) sobre el conjunto
        // EFECTIVO: roles del request + actores existentes que se conservan. Así se
        // detecta el duplicado aunque cada parte se guarde en un PUT distinto.
        var error = ValidateTraspasoPartes(instance, inputs);
        if (error is not null)
            return (null, error);

        // 5. Resolver entity ids del catálogo (deben existir por seed HU10151).
        var entityIds = new Dictionary<ParteRol, Guid>();
        foreach (var rol in providedRoles)
        {
            var entity = await catalogRepo.GetProcedureEntityByCodeAsync(RolToEntityCode[rol], ct);
            if (entity is null)
                return (null, "entity_catalog_missing");
            entityIds[rol] = entity.Id;
        }

        // 6. Upsert por rol (NO reemplazo total): se eliminan SOLO los actores cuyos roles
        // vienen en el request y se insertan los nuevos; los roles ausentes del request se
        // conservan. Así el wizard puede guardar vendedor y comprador en pasos separados sin
        // que el segundo PUT borre al primero.
        //
        // Instancia trackeada: el change tracker ve los actores quitados como Deleted y los
        // nuevos como Added. UNIQUE(procedure_instance_id, procedure_entity_id): un re-PUT del
        // mismo rol reusa su procedure_entity_id. EF Core NO garantiza orden DELETE-antes-de-INSERT
        // para la misma tabla en un SaveChanges, así que se persisten primero los DELETE y luego
        // los INSERT (dos SaveChanges).
        var providedRolesSet = providedRoles.ToHashSet();
        var toRemove = instance.Actors
            .Where(a => ParseRol(a.ActorType) is { } r && providedRolesSet.Contains(r))
            .ToList();

        // HU #10880: sujeto de identidad ANTES del reemplazo (por rol), para poder comparar el correo tras
        // el upsert. Solo interesan los roles que SÍ tenían actor previo (un rol nuevo no tiene nada que
        // reenviar: AC1 exige una validación YA enviada).
        var previousSubjectsByRol = toRemove
            .Select(a => (Rol: ParseRol(a.ActorType)!.Value, Subject: IdentitySubjectResolver.For(a)))
            .ToDictionary(x => x.Rol, x => x.Subject);

        foreach (var actor in toRemove)
            instance.Actors.Remove(actor);
        await repo.SaveChangesAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var newActorsByRol = new Dictionary<ParteRol, ProcedureInstanceActor>();
        foreach (var a in inputs)
        {
            var rol = ParseRol(a.Rol)!.Value;
            var actor = new ProcedureInstanceActor
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProcedureInstanceId = instance.Id,
                ProcedureEntityId = entityIds[rol],
                ActorType = RolToCode(rol),
                DocumentType = a.TipoDocumento.ToUpperInvariant(),
                DocumentNumber = a.NumeroDocumento.Trim(),
                FullName = a.NombreCompleto.Trim(),
                Email = a.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(a.Telefono) ? null : a.Telefono.Trim(),
                PersonType = ActorPersonTypes.Normalize(a.PersonType),
                EsRepresentanteLegal = a.EsRepresentanteLegal,
                Metadata = SerializeMetadata(a.Ciudad, a.Direccion, a.RepresentanteLegal),
                CreatedAt = now,
            };
            instance.Actors.Add(actor);
            // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
            // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
            repo.Add(actor);
            newActorsByRol[rol] = actor;
        }

        await repo.SaveChangesAsync(ct);

        // HU #10878 (ADR-0031): captura fail-safe del consentimiento Habeas Data de reúso cross-trámite.
        // Solo actores que vinieron con AutorizaReutilizacionDatos=true generan/actualizan su fila; el
        // resto (false o ausente) queda intacto — nunca degrada un `granted` previo por accidente.
        await UpsertConsentsAsync(tenantId, inputs, instance.Id, ct);

        // HU #10880 (AC1/AC2): reenvío de la validación de identidad cuando cambia el correo del sujeto.
        // Corre DESPUÉS del SaveChanges de los actores para que el correo nuevo ya esté persistido cuando
        // IniciarKyverumVerifyHandler recargue la instancia.
        await ResendIdentityOnEmailChangeAsync(instance, tenantId, previousSubjectsByRol, newActorsByRol, ct);

        return (ToResponse(instance), null);
    }

    /// <summary>
    /// HU #10878 (ADR-0031): upsert fail-safe del consentimiento de reúso cross-trámite para las
    /// partes que vinieron con <see cref="ActorInput.AutorizaReutilizacionDatos"/> = true. La llave
    /// (tenant, tipoDoc, documento) es la MISMA normalización (trim + mayúsculas) que usa
    /// <see cref="ExternalQueryCacheService"/> para el gate de reúso, así ambas coinciden. Los
    /// actores que NO autorizan (false o campo ausente) no tocan ninguna fila existente.
    /// </summary>
    private async Task UpsertConsentsAsync(
        Guid tenantId, IReadOnlyList<ActorInput> inputs, Guid procedureInstanceId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var a in inputs)
        {
            if (!a.AutorizaReutilizacionDatos)
                continue;

            var docType = a.TipoDocumento.Trim().ToUpperInvariant();
            var docNumber = a.NumeroDocumento.Trim();

            var existing = await consentRepo.GetAsync(tenantId, docType, docNumber, ct);
            if (existing is not null)
            {
                existing.Status = PersonDataConsentStatus.Granted;
                existing.ConsentVersion = PersonDataConsentRules.ConsentVersion;
                existing.ConsentSource = PersonDataConsentRules.ConsentSourceActorCapture;
                existing.GrantedAt = now;
                existing.RevokedAt = null;
                existing.SourceProcedureInstanceId = procedureInstanceId;
                existing.UpdatedAt = now;
            }
            else
            {
                await consentRepo.AddAsync(new PersonDataConsent
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    DocumentType = docType,
                    DocumentNumber = docNumber,
                    Status = PersonDataConsentStatus.Granted,
                    ConsentVersion = PersonDataConsentRules.ConsentVersion,
                    ConsentSource = PersonDataConsentRules.ConsentSourceActorCapture,
                    GrantedAt = now,
                    SourceProcedureInstanceId = procedureInstanceId,
                    CreatedAt = now,
                }, ct);
            }

            await consentRepo.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// HU #10880 — AC1: si el correo del sujeto de identidad de una parte cambió respecto al valor previo
    /// y esa parte YA tenía una validación de identidad enviada (no terminal), la expira y dispara un
    /// reenvío automático reutilizando <see cref="IniciarKyverumVerifyHandler"/> (genera un CaptureUrl
    /// nuevo y Kyverum lo envía al correo actualizado). AC2: si el correo no cambió (comparado normalizado)
    /// es no-op. Solo actúa cuando el proveedor configurado es Kyverum: el mock no emite CaptureUrl ni
    /// envía correos, así que no hay nada que "reenviar" ahí (la previa igual se conserva sin tocar).
    /// PJ (HU #10688): el sujeto es el representante legal (<see cref="IdentitySubjectResolver"/>), así que
    /// esta lógica ya compara el correo del RL, no el NIT de la empresa.
    /// </summary>
    private async Task ResendIdentityOnEmailChangeAsync(
        ProcedureInstance instance,
        Guid tenantId,
        Dictionary<ParteRol, IdentitySubject> previousSubjectsByRol,
        Dictionary<ParteRol, ProcedureInstanceActor> newActorsByRol,
        CancellationToken ct)
    {
        if (!providerOptions.IsKyverum)
            return; // mock: no hay CaptureUrl/envío real que reenviar (AC1 no aplica a este proveedor).

        foreach (var (rol, newActor) in newActorsByRol)
        {
            if (!previousSubjectsByRol.TryGetValue(rol, out var previous))
                continue; // actor nuevo para este rol: no había validación previa que reenviar.

            var newSubject = IdentitySubjectResolver.For(newActor);
            var prevEmail = NormalizeEmail(previous.Email);
            var newEmail = NormalizeEmail(newSubject.Email);
            if (prevEmail is null || newEmail is null || prevEmail == newEmail)
                continue; // AC2: sin cambio real de correo -> no-op.

            var parte = RolToCode(rol);

            // Solo hay algo que reenviar si la parte tenía una validación EN CURSO para el documento del
            // sujeto ANTERIOR (mismo doc: solo cambió el correo). Una identidad ya APROBADA NO se toca:
            // el AC habla de una validación "enviada", y expirar una aprobación obligaría a revalidar a
            // una persona que ya validó — rompiendo la radicación por corregir un correo. La aprobación
            // vigente sigue reutilizándose (HU #10350) aunque el correo cambie.
            var previa = instance.BiometricValidations.FirstOrDefault(v =>
                string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
                && v.Status is BiometricEstados.PendienteEnvio or BiometricEstados.Enviado
                    or BiometricEstados.EnProceso
                && DocCoincide(v, previous.TipoDocumento, previous.NumeroDocumento));
            if (previa is null)
                continue; // AC1 precondición: no había validación enviada -> nada que reenviar aquí.

            previa.Status = BiometricEstados.Expirado;
            previa.UpdatedAt = DateTimeOffset.UtcNow;
            await repo.SaveChangesAsync(ct);

            // Reenvío automático (CF-05): reutiliza el flujo existente de "iniciar" Kyverum. Al estar la
            // previa ya expirada, su propia guarda de idempotencia ("biometria_activa") no bloquea, y crea
            // una validación nueva con CaptureUrl nuevo enviada al correo YA actualizado del sujeto.
            var resendInput = new IniciarBiometriaInput(
                parte,
                newSubject.Nombre ?? newActor.FullName,
                newSubject.TipoDocumento ?? string.Empty,
                newSubject.NumeroDocumento ?? string.Empty,
                newSubject.Email ?? string.Empty);
            await kyverumHandler.HandleAsync(instance.Id, tenantId, resendInput, ct);
        }
    }

    /// <summary>Correo normalizado para comparar (trim + minúsculas); null si viene vacío.</summary>
    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>¿La validación corresponde al documento (tipo + número) dados? Null-safe.</summary>
    private static bool DocCoincide(ProcedureInstanceBiometricValidation v, string? tipoDoc, string? documento) =>
        !string.IsNullOrWhiteSpace(tipoDoc) && !string.IsNullOrWhiteSpace(documento)
        && string.Equals(v.DocumentNumber?.Trim(), documento.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(v.DocumentType?.Trim(), tipoDoc.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? ValidateTraspasoPartes(ProcedureInstance instance, IReadOnlyList<ActorInput> inputs)
    {
        var vendedor = EffectiveParte(instance, inputs, ParteRol.Vendedor);
        var comprador = EffectiveParte(instance, inputs, ParteRol.Comprador);
        if (vendedor is null || comprador is null)
            return null;

        var dup = TraspasoPartes.DetectarDuplicadas(vendedor, comprador);
        return TraspasoPartes.MensajeDuplicadas(dup) is null ? null : "partes_duplicadas";
    }

    /// <summary>
    /// Datos efectivos de un rol tras el upsert: el del request si viene en él; si no, el del
    /// actor ya persistido que se conservará. Permite validar vendedor≠comprador aunque cada
    /// parte se guarde en un PUT distinto.
    /// </summary>
    private static ParteDatos? EffectiveParte(
        ProcedureInstance instance,
        IReadOnlyList<ActorInput> inputs,
        ParteRol rol)
    {
        var input = inputs.FirstOrDefault(a => ParseRol(a.Rol) == rol);
        if (input is not null)
            return new ParteDatos(input.NombreCompleto, input.NumeroDocumento, input.Email);

        var existing = instance.Actors.FirstOrDefault(a => ParseRol(a.ActorType) == rol);
        return existing is null
            ? null
            : new ParteDatos(existing.FullName, existing.DocumentNumber, existing.Email ?? string.Empty);
    }

    private static TipologiaJourney? ResolveJourney(ProcedureInstance instance)
    {
        // Preferir tipología si está set; si no, resolver el journey por modalidad_entrada.
        var byTipologia = TipologiaMatrizCatalog.Get(instance.TipologiaCodigo);
        if (byTipologia is not null)
            return byTipologia;

        var modalidad = TramiteModalidadEntradaCodes.FromCode(instance.ModalidadEntrada);
        return modalidad is null
            ? null
            : TipologiaMatrizCatalog.All.FirstOrDefault(j => j.Modalidad == modalidad.Value);
    }

    private static ParteRol? ParseRol(string? rol) => rol?.Trim().ToLowerInvariant() switch
    {
        "comprador" => ParteRol.Comprador,
        "vendedor" => ParteRol.Vendedor,
        _ => null,
    };

    private static string RolToCode(ParteRol rol) => rol switch
    {
        ParteRol.Comprador => "comprador",
        ParteRol.Vendedor => "vendedor",
        _ => rol.ToString().ToLowerInvariant(),
    };

    internal static ActorsResponse ToResponse(ProcedureInstance instance) =>
        new(instance.Actors
            .Select(a =>
            {
                var (ciudad, direccion, rl) = ParseMetadata(a.Metadata);
                return new ActorDto(
                    a.ActorType,
                    a.DocumentType,
                    a.DocumentNumber,
                    a.FullName,
                    a.Email ?? string.Empty,
                    a.Phone,
                    ciudad,
                    direccion,
                    a.PersonType,
                    a.EsRepresentanteLegal,
                    rl);
            })
            .ToList());

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializa ciudad/dirección + representante legal al JSON de <c>actor.metadata</c>.
    /// Sin ningún dato → "{}".
    /// </summary>
    private static string SerializeMetadata(string? ciudad, string? direccion, ActorRepresentanteLegal? rl)
    {
        var c = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim();
        var d = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();
        var repLegal = NormalizeRepresentanteLegal(rl);
        return c is null && d is null && repLegal is null
            ? "{}"
            : JsonSerializer.Serialize(new ActorMetadata(c, d, repLegal), MetadataJson);
    }

    /// <summary>
    /// Lee ciudad/dirección + representante legal de <c>actor.metadata</c>. Robusto ante
    /// null/"{}"/JSON inválido.
    /// </summary>
    private static (string? Ciudad, string? Direccion, ActorRepresentanteLegal? RepresentanteLegal) ParseMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "{}")
            return (null, null, null);
        try
        {
            var m = JsonSerializer.Deserialize<ActorMetadata>(metadata, MetadataJson);
            return (m?.Ciudad, m?.Direccion, NormalizeRepresentanteLegal(m?.RepresentanteLegal));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    /// <summary>Trim + colapso a null si el representante legal viene vacío en todos sus campos.</summary>
    private static ActorRepresentanteLegal? NormalizeRepresentanteLegal(ActorRepresentanteLegal? rl)
    {
        if (rl is null)
            return null;
        string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var tipo = Clean(rl.TipoDocumento);
        var numero = Clean(rl.NumeroDocumento);
        var nombre = Clean(rl.NombreCompleto);
        var email = Clean(rl.Email);
        var telefono = Clean(rl.Telefono);
        return tipo is null && numero is null && nombre is null && email is null && telefono is null
            ? null
            : new ActorRepresentanteLegal(tipo, numero, nombre, email, telefono);
    }

    private sealed record ActorMetadata(
        string? Ciudad,
        string? Direccion,
        ActorRepresentanteLegal? RepresentanteLegal = null);
}

/// <summary>GET de actores del set guardado.</summary>
public sealed class GetActorsHandler(IProcedureInstanceRepository repo)
{
    public async Task<(ActorsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        return (PutActorsHandler.ToResponse(instance), null);
    }
}
