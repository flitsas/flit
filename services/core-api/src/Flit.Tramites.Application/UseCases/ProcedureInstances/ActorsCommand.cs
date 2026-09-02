using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Integration;
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
/// <remarks>
/// ADR-0053 (Múltiple Propietario): <see cref="Ordinal"/> (1..4, default 1) identifica al actor
/// dentro de su <see cref="Rol"/> — <c>1</c> es el principal/solidario (el que existía antes de
/// ADR-0053; omitirlo cuando el request incluye su rol es <c>ordinal_principal_ausente</c>).
/// <see cref="Porcentaje"/> (2 decimales) solo es exigido por el backend cuando el conjunto EFECTIVO
/// del rol (request ∪ conservados) tiene 2 o más actores; con un solo actor se ignora y se persiste
/// <c>null</c> (comportamiento previo, sin bloque de reparto). Omitir ambos campos equivale al
/// contrato anterior a ADR-0053: un actor por rol, <c>ordinal=1</c>, sin porcentaje.
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
    ActorMandante? Mandante = null,
    bool AutorizaReutilizacionDatos = false,
    int Ordinal = 1,
    decimal? Porcentaje = null);

/// <remarks>ADR-0053: ver <see cref="ActorInput"/> — mismos campos <see cref="Ordinal"/>/<see cref="Porcentaje"/>, siempre presentes en la respuesta.</remarks>
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
    ActorRepresentanteLegal? RepresentanteLegal = null,
    ActorMandante? Mandante = null,
    int Ordinal = 1,
    decimal? Porcentaje = null);

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
    string? Telefono,
    // HU #11061 — mecanismo de firma ELEGIDO por el gestor cuando el representante tiene el baúl y la
    // identidad vigentes a la vez. null = sin elección ⇒ precedencia del baúl (HU #11031).
    string? MecanismoFirma = null);

/// <summary>
/// Mandante / poderdante de una parte (contrato de integración con terceros: <c>principal_mandante</c>).
/// Igual que el representante legal, es información de contacto embebida en <c>actor.metadata</c>
/// (sin DDL): no participa en biométrica ni en los gates del wizard.
/// </summary>
public sealed record ActorMandante(
    string? TipoDocumento,
    string? NumeroDocumento,
    string? NombreCompleto,
    string? Email);

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
    IPersonDataConsentRepository consentRepo,
    ISignatureVaultPolicy? vaultPolicy = null,
    ILogger<PutActorsHandler>? logger = null)
{
    // HU #11665 — traza del disparador de identidad. Default inerte para no obligar a los tests que no
    // la ejercitan a inyectarlo. NUNCA se loguea PII: ni correo, ni documento, ni nombre.
    private readonly ILogger _logger = logger ?? NullLogger<PutActorsHandler>.Instance;

    // Baúl de firmas: lo consume el reenvío por cambio de correo (HU #10880), que no puede expirar la
    // validación en curso de una parte que va a firmar con el baúl. Default inerte (nunca resuelve
    // firma) ⇒ los tests que no lo inyectan conservan su comportamiento.
    private readonly ISignatureVaultPolicy _vaultPolicy = vaultPolicy ?? NullSignatureVaultPolicy.Instance;

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
            // El código ya existía en el catálogo («Arrendatario»); lo que faltaba era poder llegar a él.
            [ParteRol.Locatario] = "LESSEE",
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

        // Subsanación (flag sobre rechazado) o borrador: actores editables. Fuera de eso, bloqueado.
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft");

        var inputs = request.Actors ?? [];

        // 1. Validación de forma de cada actor (documento/email/rol conocidos).
        foreach (var a in inputs)
        {
            var rol = ParseRol(a.Rol);
            if (rol is null)
                return (null, "invalid_rol");
            // ADR-0053: posición del actor dentro de su rol, 1..4 (1 = principal/solidario).
            if (a.Ordinal is < 1 or > 4)
                return (null, "ordinal_fuera_de_rango");
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
            //
            // El disparador sigue siendo lo DECLARADO, no el documento: exigirlo a todo NIT rompería
            // el leasing, donde el arrendatario es una empresa que no firma y de la que nunca se
            // captura representante legal (ver LocatarioPartePropiaTests). El correo del RL se pide
            // porque el RL es el sujeto de identidad, no por tener NIT — son dos cosas distintas y
            // la normalización de `person_type` de más abajo no debe arrastrar esta.
            if (ActorPersonTypes.IsJuridical(a.PersonType))
            {
                var rlEmail = a.RepresentanteLegal?.Email;
                if (string.IsNullOrWhiteSpace(rlEmail) || !TramiteDocumento.EmailValido(rlEmail))
                    return (null, "rl_email_requerido");
            }
        }

        // 2. Roles permitidos según modalidad_entrada (matriz de dominio, no hardcode).
        var allowedRoles = AllowedRoles(instance);

        // ADR-0053: un rol admite ahora 1..4 actores (`providedRoles` deja de exigir unicidad de
        // rol dentro del request — eso lo impone ahora `ordinal`, no la presencia del rol).
        var providedRoles = new List<ParteRol>();
        foreach (var a in inputs)
        {
            var rol = ParseRol(a.Rol)!.Value;
            if (!allowedRoles.Contains(rol))
                return (null, "rol_not_allowed");
            if (!providedRoles.Contains(rol))
                providedRoles.Add(rol);
        }

        // 2b. ADR-0053 — dentro de cada rol presente en el request: ordinal único (el índice de BD
        // solo lo garantiza tras el SaveChanges; aquí se atrapa antes) y el principal (ordinal=1) no
        // puede faltar — el upsert por rol BORRA todos los actores del rol y el PUT los reemplaza
        // por el conjunto enviado, así que omitir ordinal=1 lo eliminaría.
        foreach (var rol in providedRoles)
        {
            var ordinals = inputs
                .Where(a => ParseRol(a.Rol) == rol)
                .Select(a => a.Ordinal)
                .ToList();
            if (ordinals.Distinct().Count() != ordinals.Count)
                return (null, "ordinal_fuera_de_rango"); // dos actores del mismo rol con el mismo ordinal.
            if (!ordinals.Contains(1))
                return (null, "ordinal_principal_ausente");
        }

        // 3. PUT incremental (upsert por rol): el wizard guarda un rol por paso
        // (vendedor en el paso 3, comprador en el paso 4). NO se exige aquí la
        // completitud de roles obligatorios — eso lo validan los gates de pasos 3–4
        // y el SubmitGate al finalizar. Por eso ya no hay check de "missing_required_rol".

        // 3b. ADR-0053 §4.1/§4.5 — reparto de propiedad sobre el conjunto EFECTIVO por rol (request ∪
        // conservados): con 1 actor no se exige porcentaje (se ignora si viene); con 2+, todos deben
        // traer porcentaje > 0 y la suma debe ser EXACTAMENTE 100.00. Autoritativo: el backend nunca
        // confía en el cálculo del frontend (auto-absorción del residuo, que vive solo ahí).
        var error = ValidateShares(instance, inputs, providedRoles);
        if (error is not null)
            return (null, error);

        // 3c. ADR-0053 §4.4 nivel 1 — duplicidad INTRA-lado: bloqueada SIEMPRE, sin condición de
        // conteo, sobre cada rol presente en el request (los roles conservados ya se validaron en su
        // propio PUT).
        error = ValidateDuplicadosIntraLado(instance, inputs, providedRoles);
        if (error is not null)
            return (null, error);

        // 4. Unicidad vendedor ≠ comprador (documento y email) sobre el conjunto
        // EFECTIVO: roles del request + actores existentes que se conservan. Así se
        // detecta el duplicado aunque cada parte se guarde en un PUT distinto.
        //
        // ADR-0053 §4.4 nivel 2 — CONFIRMADO: esta comparación cruzada solo se evalúa cuando AMBOS
        // lados quedan en exactamente 1 actor efectivo (ver ValidateTraspasoPartes). Con cualquier
        // lado en 2+, se omite a propósito (copropietario que concurre a la venta y compra una cuota
        // adicional) — el `if` que envuelve la llamada NO toca la llamada en sí, así que el caso 1-a-1
        // (mayoritario) ejecuta la MISMA ruta, en el MISMO orden, byte a byte.
        error = ValidateTraspasoPartes(instance, inputs);
        if (error is not null)
            return (null, error);

        // Mismo criterio de conjunto efectivo para el arrendatario: no puede ser el propietario.
        // ADR-0053 §4.4 — el locatario se deja FUERA del ajuste de nivel 2 (no se relaja): sigue
        // comparándose 1-a-1 exactamente como antes.
        error = ValidateLocatario(instance, inputs);
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

        // HU #10880: sujeto de identidad ANTES del reemplazo (por rol + ordinal), para poder comparar
        // el correo tras el upsert. Solo interesan los roles que SÍ tenían actor previo (un rol nuevo
        // no tiene nada que reenviar: AC1 exige una validación YA enviada).
        //
        // ADR-0053 — antes de Múltiple Propietario cada rol tenía a lo sumo un actor `toRemove`, así
        // que un `Dictionary<ParteRol, IdentitySubject>` bastaba. Con 2..4 actores por rol, esa clave
        // ya no es única (dos filas `toRemove` del mismo rol lanzarían "An item with the same key has
        // already been added"): la clave pasa a (rol, ordinal) — el ordinal es único dentro del rol
        // por construcción (índice de BD) — para correlacionar cada actor previo con el nuevo que
        // ocupa la MISMA posición tras el upsert (ver <see cref="ResendIdentityOnEmailChangeAsync"/>).
        var previousSubjectsByRolOrdinal = toRemove
            .Select(a => (Rol: ParseRol(a.ActorType)!.Value, a.Ordinal, Subject: IdentitySubjectResolver.For(a)))
            .ToDictionary(x => (x.Rol, x.Ordinal), x => x.Subject);

        foreach (var actor in toRemove)
            instance.Actors.Remove(actor);
        await repo.SaveChangesAsync(ct);

        // ADR-0053 §4.1/§4.5 — cuántos actores efectivos tendrá cada rol tras este PUT: si el rol viene
        // en el request, es la cuenta de inputs de ese rol (el rol se reemplaza completo); ya validado
        // en ValidateShares que con 2+ todos traen porcentaje > 0 sumando 100.00.
        var countByRol = inputs
            .GroupBy(a => ParseRol(a.Rol)!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var now = DateTimeOffset.UtcNow;
        var newActorsByRol = new Dictionary<ParteRol, List<ProcedureInstanceActor>>();
        foreach (var a in inputs)
        {
            var rol = ParseRol(a.Rol)!.Value;
            // Con un solo actor efectivo en el rol, el porcentaje se IGNORA (persiste null) aunque el
            // request traiga un valor — comportamiento previo a ADR-0053, sin bloque de reparto.
            var porcentaje = countByRol[rol] <= 1
                ? (decimal?)null
                : Math.Round(a.Porcentaje!.Value, 2, MidpointRounding.AwayFromZero);
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
                PersonType = ActorPersonTypes.ResolveForDocument(a.TipoDocumento, a.PersonType),
                EsRepresentanteLegal = a.EsRepresentanteLegal,
                Metadata = SerializeMetadata(a.Ciudad, a.Direccion, a.RepresentanteLegal, a.Mandante),
                Ordinal = a.Ordinal,
                OwnershipPercentage = porcentaje,
                CreatedAt = now,
            };
            instance.Actors.Add(actor);
            // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
            // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
            repo.Add(actor);
            if (!newActorsByRol.TryGetValue(rol, out var list))
            {
                list = [];
                newActorsByRol[rol] = list;
            }
            list.Add(actor);
        }

        await repo.SaveChangesAsync(ct);

        // HU #10878 (ADR-0031): captura fail-safe del consentimiento Habeas Data de reúso cross-trámite.
        // Solo actores que vinieron con AutorizaReutilizacionDatos=true generan/actualizan su fila; el
        // resto (false o ausente) queda intacto — nunca degrada un `granted` previo por accidente.
        await UpsertConsentsAsync(tenantId, inputs, instance.Id, ct);

        // HU #10880 (AC1/AC2): reenvío de la validación de identidad cuando cambia el correo del sujeto.
        // Corre DESPUÉS del SaveChanges de los actores para que el correo nuevo ya esté persistido cuando
        // IniciarKyverumVerifyHandler recargue la instancia.
        await ResendIdentityOnEmailChangeAsync(instance, tenantId, previousSubjectsByRolOrdinal, newActorsByRol, ct);

        // HU #11662: validación de identidad del representante legal de una parte jurídica. Corre DESPUÉS
        // del reenvío por cambio de correo para que, si ese ya creó una validación nueva, la precedencia
        // río abajo la vea en vuelo y no duplique el envío.
        await EnviarValidacionAlRepresentanteDeLaParteJuridicaAsync(instance, tenantId, newActorsByRol, ct);

        return (ToResponse(instance), null);
    }

    /// <summary>
    /// HU #11195/#11662 — al registrar el actor, si la parte es una persona jurídica se encamina la
    /// validación de identidad del <b>representante legal declarado en el trámite</b>. Sin este
    /// disparador el gestor se quedaba sin salida: el trámite exige la firma de alguien a quien nadie le
    /// había pedido validar su identidad.
    ///
    /// <para><b>Aquí solo se filtran DATOS, no se decide el envío.</b> Lo que queda es: proveedor
    /// Kyverum, actor jurídico, y un sujeto que sea el representante legal con tipo y número de documento
    /// y correo. Si falta cualquiera de esos, no hay a quién escribirle ni a quién validar (el NIT no es
    /// validable biométricamente) y se omite, igual que hace <c>EnsureIdentityHandler</c> con
    /// <c>sin_actor</c>.</para>
    ///
    /// <para><b>Por qué ya no hay compuertas de negocio (HU #11662).</b> Este método llegó a descartar
    /// envíos por su cuenta —cobertura del baúl, y si la COMPAÑÍA tenía algún representante utilizable en
    /// el directorio—. Lo segundo respondía por la empresa y no por la persona elegida para este trámite:
    /// bastaba que otro representante acreditado tuviera firma para que al representante elegido no le
    /// llegara nada, dejando el trámite sin identidad y sin vía para conseguirla. Y ambas comprobaciones
    /// eran redundantes: <see cref="IniciarKyverumVerifyHandler"/> evalúa río abajo la precedencia única
    /// de envío (ADR-0039: baúl → identidad vigente → validación en vuelo → enviar), de la cual la
    /// cobertura del baúl es literalmente el primer paso. Un prechequeo solo puede suprimir envíos
    /// legítimos; nunca añade uno que la precedencia no fuera a hacer.</para>
    ///
    /// <para><b>Por qué la validación del trámite y no <c>POST identity/send</c> de Admin:</b> ese
    /// endpoint exige un representante YA REGISTRADO en el directorio, que es justo lo que no existe en
    /// el caso principal. El RL vive declarado en <c>actor.metadata</c>, así que se usa la misma
    /// maquinaria que el reenvío por cambio de correo (HU #10880) sobre el sujeto de identidad de la
    /// parte. La validación queda en el trámite y los gates de identidad la ven.</para>
    ///
    /// <para>Las personas naturales ni siquiera entran. Solo actúa con Kyverum: el proveedor mock no
    /// emite CaptureUrl ni envía correos, mismo criterio que
    /// <see cref="ResendIdentityOnEmailChangeAsync"/>.</para>
    ///
    /// <para><b>HU #11665 — ninguna omisión es muda.</b> Cada salida sin envío deja un motivo tipificado
    /// (<see cref="EnvioValidacionBloqueoRules"/>) en un log de negocio, y el listado de biometría
    /// publica el mismo código calculado con la misma regla. El motivo no se persiste: se deriva del
    /// estado, así que desaparece solo en cuanto el gestor corrige el dato.</para>
    /// </summary>
    private async Task EnviarValidacionAlRepresentanteDeLaParteJuridicaAsync(
        ProcedureInstance instance,
        Guid tenantId,
        Dictionary<ParteRol, List<ProcedureInstanceActor>> newActorsByRol,
        CancellationToken ct)
    {
        var rolesQueValidan = RolesQueValidanIdentidad(instance);

        // ADR-0053 §4.1 — mismo cuerpo que antes de Múltiple Propietario, solo cambia la cardinalidad
        // del bucle: cada actor del rol (1..4, `OrderBy(Ordinal)` para que el principal dispare
        // primero) pasa por la MISMA regla, no solo el `ordinal=1`.
        foreach (var (rol, actor) in newActorsByRol.SelectMany(
            kv => kv.Value.OrderBy(a => a.Ordinal).Select(actor => (kv.Key, actor))))
        {
            // Una parte que el tipo NO manda a validar no convoca validación de nadie, ni siquiera de
            // su representante legal. Sin este corte, un locatario persona jurídica arrastraba a su
            // representante a una biometría que el trámite no le pide.
            if (rolesQueValidan is not null && !rolesQueValidan.Contains(rol))
                continue;

            var subject = IdentitySubjectResolver.For(actor);

            // HU #11665 — el filtrado de datos vive en la regla compartida, no en una condición
            // compuesta local. Las tres omisiones por datos incompletos eran UN SOLO `continue`, así
            // que ni el código sabía cuál de las tres había ocurrido; y el corte por proveedor mock
            // hacía `return` del método entero, con lo que las partes siguientes ni se miraban.
            var estado = EnvioValidacionBloqueoRules.EstadoDe(actor, subject, providerOptions.IsKyverum);
            if (!estado.ActorEsJuridico)
                continue; // Persona natural: no entra al disparador y no reporta motivo.

            var motivo = EnvioValidacionBloqueoRules.Evaluar(estado);
            if (motivo is not null)
            {
                PutActorsLog.ValidacionNoEnviada(_logger, instance.Id, RolToCode(rol), motivo.Codigo);
                continue;
            }

            // Quién decide si de verdad se envía: el handler, que evalúa la precedencia única
            // (ADR-0039). Si la persona ya está cubierta por el baúl, ya tiene identidad vigente o ya
            // tiene una validación en vuelo, devuelve la decisión y no crea nada. De ESA decisión —no
            // de un pre-chequeo local, que la HU #11662 retiró— salen los motivos informativos.
            var (_, _, conflicto) = await kyverumHandler.HandleAsync(
                instance.Id,
                tenantId,
                new IniciarBiometriaInput(
                    RolToCode(rol),
                    subject.Nombre ?? actor.FullName,
                    subject.TipoDocumento!,
                    subject.NumeroDocumento!,
                    subject.Email!),
                ct);

            var informativo = EnvioValidacionBloqueoRules.DesdeDecision(conflicto);
            if (informativo is not null)
                PutActorsLog.ValidacionNoEnviada(_logger, instance.Id, RolToCode(rol), informativo.Codigo);
        }
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
    ///
    /// <para><b>ADR-0053</b> — la correlación "actor previo → actor nuevo" pasa de por-rol a
    /// por-(rol, ordinal): el upsert reemplaza TODO el rol de una vez, así que el actor previo que
    /// ocupaba el `ordinal` N es, por construcción, el que se está reemplazando por el nuevo actor
    /// del MISMO `ordinal` (misma posición de pestaña en el formulario). Con un solo actor por rol
    /// (caso mayoritario) la clave sigue siendo, en la práctica, "el actor del rol" — cero regresión.</para>
    /// </summary>
    private async Task ResendIdentityOnEmailChangeAsync(
        ProcedureInstance instance,
        Guid tenantId,
        Dictionary<(ParteRol Rol, int Ordinal), IdentitySubject> previousSubjectsByRolOrdinal,
        Dictionary<ParteRol, List<ProcedureInstanceActor>> newActorsByRol,
        CancellationToken ct)
    {
        if (!providerOptions.IsKyverum)
            return; // mock: no hay CaptureUrl/envío real que reenviar (AC1 no aplica a este proveedor).

        foreach (var (rol, newActor) in newActorsByRol.SelectMany(
            kv => kv.Value.Select(actor => (kv.Key, actor))))
        {
            if (!previousSubjectsByRolOrdinal.TryGetValue((rol, newActor.Ordinal), out var previous))
                continue; // ordinal nuevo en este rol: no había validación previa que reenviar.

            var newSubject = IdentitySubjectResolver.For(newActor);
            var prevEmail = NormalizeEmail(previous.Email);
            var newEmail = NormalizeEmail(newSubject.Email);
            if (prevEmail is null || newEmail is null || prevEmail == newEmail)
                continue; // AC2: sin cambio real de correo -> no-op.

            // Con la firma del baúl cubriendo a la parte, corregir el correo no debe convocar una
            // validación de identidad: se firma con el baúl. La previa en curso se deja como está —
            // expirarla aquí sería decidir por el gestor sobre una validación que él inició.
            if (await LaFirmaDelBaulYaCubreAsync(tenantId, newActor, newSubject, ct))
                continue;

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

    /// <summary>
    /// ¿La firma del baúl ya cubre a esta parte, de modo que pedirle una validación de identidad sería
    /// pedirle algo que no necesita?
    ///
    /// <para>Lo consume el <b>reenvío por cambio de correo</b> (HU #10880), y ahí no es redundante con
    /// la precedencia única: antes de llamar al handler, ese camino EXPIRA la validación en curso. Sin
    /// esta comprobación, corregirle el correo a una parte que va a firmar con el baúl le tumbaría una
    /// validación que el gestor inició a propósito.</para>
    ///
    /// <para>Se comprueban las dos cosas: que la firma del baúl proceda para esta parte
    /// (<see cref="FirmaBaulCobertura.Aplica"/>, que es el predicado único del Bug #11141 e incluye el
    /// mecanismo elegido) y que exista de verdad. Sin firma real no basta con haberla elegido: ahí la
    /// validación de identidad sigue siendo la única salida.</para>
    ///
    /// <para><b>HU #11662:</b> el disparador de la parte jurídica ya NO lo usa. Ahí sí era redundante —
    /// la cobertura del baúl es el primer paso de la precedencia que evalúa
    /// <see cref="IniciarKyverumVerifyHandler"/>— y solo servía para suprimir envíos legítimos.</para>
    /// </summary>
    private async Task<bool> LaFirmaDelBaulYaCubreAsync(
        Guid tenantId, ProcedureInstanceActor actor, IdentitySubject subject, CancellationToken ct)
    {
        if (!FirmaBaulCobertura.Aplica(actor)
            || string.IsNullOrWhiteSpace(subject.TipoDocumento)
            || string.IsNullOrWhiteSpace(subject.NumeroDocumento))
            return false;

        return await _vaultPolicy
            .ResolveAsync(tenantId, subject.TipoDocumento.Trim(), subject.NumeroDocumento.Trim(), ct)
            .ConfigureAwait(false) is not null;
    }

    /// <summary>Correo normalizado para comparar (trim + minúsculas); null si viene vacío.</summary>
    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>¿La validación corresponde al documento (tipo + número) dados? Null-safe.</summary>
    private static bool DocCoincide(ProcedureInstanceBiometricValidation v, string? tipoDoc, string? documento) =>
        !string.IsNullOrWhiteSpace(tipoDoc) && !string.IsNullOrWhiteSpace(documento)
        && string.Equals(v.DocumentNumber?.Trim(), documento.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(v.DocumentType?.Trim(), tipoDoc.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// ADR-0053 §4.4 nivel 2 — CONFIRMADO: vendedor≠comprador solo se evalúa cuando el conjunto
    /// EFECTIVO deja EXACTAMENTE 1 actor en cada lado. Con cualquier lado en 2+, la comprobación
    /// cruzada se omite (copropietario que concurre a la venta y compra una cuota adicional). El
    /// bloque <c>if</c> envuelve, SIN modificar, la llamada que ya existía a
    /// <see cref="TraspasoPartes.DetectarDuplicadas"/>/<see cref="TraspasoPartes.MensajeDuplicadas"/>:
    /// en el caso 1-a-1 (mayoritario) esta función ejecuta exactamente la misma ruta de antes.
    /// </summary>
    private static string? ValidateTraspasoPartes(ProcedureInstance instance, IReadOnlyList<ActorInput> inputs)
    {
        var vendedores = EffectivePartes(instance, inputs, ParteRol.Vendedor);
        var compradores = EffectivePartes(instance, inputs, ParteRol.Comprador);
        if (vendedores.Count != 1 || compradores.Count != 1)
            return null;

        var dup = TraspasoPartes.DetectarDuplicadas(vendedores[0], compradores[0]);
        return TraspasoPartes.MensajeDuplicadas(dup) is null ? null : "partes_duplicadas";
    }

    /// <summary>Error: el arrendatario y el propietario resultaron ser la misma persona.</summary>
    public const string LocatarioIgualAlPropietarioError = "locatario_igual_al_propietario";

    /// <summary>
    /// El locatario no puede ser el propietario. Si lo fueran, en un CAMBIO_LOCATARIO no habría nada
    /// que cambiar y en una matrícula por leasing no habría leasing — y el FUR imprimiría «de X a X».
    ///
    /// <para>La comprobación vive aquí y no en el formulario porque las dos partes se capturan en
    /// PASOS DISTINTOS: la pantalla del locatario nunca tiene al propietario a la vista. Se evalúa
    /// sobre el conjunto EFECTIVO (lo que trae el request + los actores que se conservan), igual que
    /// la regla de vendedor≠comprador, así que da lo mismo en qué orden se guarden.</para>
    ///
    /// <para><b>ADR-0053</b> — el locatario se deja FUERA de la relajación de nivel 2 (§4.4): el
    /// encargo cerrado habla de vendedores/compradores como los dos repartos independientes, sin
    /// mencionar al locatario. Esta comprobación sigue siendo estrictamente 1-a-1 (igual que antes);
    /// con cualquier lado en 2+ actores, simplemente no se evalúa — no hay hoy un flujo de negocio que
    /// capture un locatario o un "propietario" (rol comprador en la familia OTROS) con copropiedad.</para>
    /// </summary>
    private static string? ValidateLocatario(ProcedureInstance instance, IReadOnlyList<ActorInput> inputs)
    {
        var locatarios = EffectivePartes(instance, inputs, ParteRol.Locatario);
        var propietarios = EffectivePartes(instance, inputs, ParteRol.Comprador);
        if (locatarios.Count != 1 || propietarios.Count != 1)
            return null;

        var locatario = locatarios[0];
        var propietario = propietarios[0];

        // Se compara el NÚMERO, que es lo que `ParteDatos` transporta y el mismo criterio con el que
        // `TraspasoPartes` decide que vendedor y comprador son la misma persona.
        var mismoDocumento =
            !string.IsNullOrWhiteSpace(locatario.Documento)
            && string.Equals(locatario.Documento.Trim(), propietario.Documento?.Trim(), StringComparison.Ordinal);

        return mismoDocumento ? LocatarioIgualAlPropietarioError : null;
    }

    /// <summary>
    /// ADR-0053 §4.1/§4.5 — reparto de propiedad sobre el conjunto EFECTIVO de cada rol presente en
    /// el request. Autoritativo: el backend nunca confía en el cálculo del frontend (la auto-absorción
    /// del residuo en <c>ordinal=1</c> es UX que vive solo ahí — aquí solo se valida el RESULTADO).
    /// </summary>
    private static string? ValidateShares(
        ProcedureInstance instance, IReadOnlyList<ActorInput> inputs, IReadOnlyList<ParteRol> providedRoles)
    {
        foreach (var rol in providedRoles)
        {
            var efectivos = EffectiveShares(instance, inputs, rol);
            if (efectivos.Count <= 1)
                continue; // 1 actor en el rol: el porcentaje se ignora, sin bloque de reparto (§4.5).

            if (efectivos.Any(x => x.Porcentaje is null or <= 0m))
                return "porcentaje_en_cero";

            var suma = efectivos.Sum(x => x.Porcentaje!.Value);
            if (Math.Round(suma, 2, MidpointRounding.AwayFromZero) != 100.00m)
                return "porcentajes_no_suman_100";
        }

        return null;
    }

    /// <summary>
    /// ADR-0053 §4.4 nivel 1 — duplicidad intra-lado: BLOQUEADA SIEMPRE, sin condición de conteo, para
    /// cada rol presente en el request (los roles conservados intactos ya se validaron en su propio
    /// PUT y no pueden haber quedado con duplicados). Código de error: <c>actor_duplicado_mismo_lado</c>.
    /// </summary>
    private static string? ValidateDuplicadosIntraLado(
        ProcedureInstance instance, IReadOnlyList<ActorInput> inputs, IReadOnlyList<ParteRol> providedRoles)
    {
        foreach (var rol in providedRoles)
        {
            var efectivos = EffectivePartes(instance, inputs, rol);
            if (TraspasoPartes.DetectarDuplicadosIntraLado(efectivos))
                return "actor_duplicado_mismo_lado";
        }

        return null;
    }

    /// <summary>
    /// Datos efectivos (nombre/documento/email/contacto) de TODOS los actores de un rol tras el
    /// upsert: los del request si el rol viene en él (el upsert reemplaza el rol COMPLETO); si no, los
    /// de los actores ya persistidos que se conservarán, en orden de <c>Ordinal</c>. Generaliza el
    /// antiguo <c>EffectiveParte</c> (que devolvía un único <see cref="ParteDatos"/>) a una lista —
    /// ADR-0053, hasta 4 actores por rol. Permite validar vendedor≠comprador/duplicidad intra-lado
    /// aunque cada parte se guarde en un PUT distinto.
    /// </summary>
    private static List<ParteDatos> EffectivePartes(
        ProcedureInstance instance,
        IReadOnlyList<ActorInput> inputs,
        ParteRol rol)
    {
        var fromRequest = inputs.Where(a => ParseRol(a.Rol) == rol).ToList();
        if (fromRequest.Count > 0)
            return fromRequest
                .Select(input => new ParteDatos(
                    input.NombreCompleto, input.NumeroDocumento, input.Email,
                    input.Ciudad, input.Direccion, input.Telefono))
                .ToList();

        // HU #11593 — ciudad/dirección viven en actor.metadata (JSON); el teléfono en la columna.
        return instance.Actors
            .Where(a => ParseRol(a.ActorType) == rol)
            .OrderBy(a => a.Ordinal)
            .Select(existing =>
            {
                var (ciudad, direccion, _, _) = ActorMetadataReader.Parse(existing.Metadata);
                return new ParteDatos(
                    existing.FullName, existing.DocumentNumber, existing.Email ?? string.Empty,
                    ciudad, direccion, existing.Phone);
            })
            .ToList();
    }

    /// <summary>
    /// ADR-0053 §4.1/§4.5 — ordinal + porcentaje EFECTIVOS de todos los actores de un rol tras el
    /// upsert, mismo criterio de "request si viene, si no lo conservado" que <see cref="EffectivePartes"/>.
    /// </summary>
    private static List<(int Ordinal, decimal? Porcentaje)> EffectiveShares(
        ProcedureInstance instance,
        IReadOnlyList<ActorInput> inputs,
        ParteRol rol)
    {
        var fromRequest = inputs.Where(a => ParseRol(a.Rol) == rol).ToList();
        if (fromRequest.Count > 0)
            return fromRequest.Select(a => (a.Ordinal, a.Porcentaje)).ToList();

        return instance.Actors
            .Where(a => ParseRol(a.ActorType) == rol)
            .OrderBy(a => a.Ordinal)
            .Select(a => (a.Ordinal, a.OwnershipPercentage))
            .ToList();
    }

    /// <summary>
    /// Roles de actor admitidos por el trámite (ADR-0050): los declara el <c>gate_profile</c> del
    /// tipo, no un catálogo de journeys por modalidad.
    /// <para>En la familia OTROS interviene un solo actor —el titular, que no vende ni compra— y se
    /// persiste con el rol <see cref="ParteRol.Comprador"/>, igual que en matrícula inicial, porque
    /// el modelo no tiene un rol de propietario.</para>
    /// </summary>
    private static HashSet<ParteRol> AllowedRoles(ProcedureInstance instance)
    {
        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);

        var roles = new HashSet<ParteRol>();
        if (profile.RequiresBuyer)
            roles.Add(ParteRol.Comprador);
        if (profile.RequiresSeller)
            roles.Add(ParteRol.Vendedor);
        if (profile.RequiresLessee)
            roles.Add(ParteRol.Locatario);
        return roles;
    }

    /// <summary>
    /// Roles que este tipo manda a VALIDAR IDENTIDAD, según <c>biometricActors</c> del perfil.
    ///
    /// <para>Existe porque el disparador de la validación del representante legal recorría TODOS los
    /// actores guardados: con un locatario persona jurídica habría convocado una validación para su
    /// representante, cuando en el leasing quien valida y firma es el propietario. La lista es la
    /// misma que el motor usa para los gates, así que pantalla, gate y disparador no pueden discrepar.</para>
    ///
    /// <para>Un perfil SIN <c>biometricActors</c> devuelve <c>null</c> y el llamador no filtra nada:
    /// es el comportamiento previo, y degradar a «ninguno» apagaría el disparador en todo tipo cuyo
    /// perfil llegue vacío o corrupto.</para>
    ///
    /// <para>ADR-0051 — expuesto como <c>internal</c> para que <see cref="BiometricaCommand"/>
    /// (<c>ListBiometriaHandler</c>) lo reutilice en vez de escribir una tercera traducción de
    /// <c>biometricActors</c> → rol interno.</para>
    /// </summary>
    internal static HashSet<ParteRol>? RolesQueValidanIdentidad(ProcedureInstance instance)
    {
        var profile = ProcedureTypeGateProfile.FromJson(instance.ProcedureType?.GateProfile);
        if (profile.BiometricActors.Count == 0)
            return null;

        var codigos = profile.BiometricActors
            .Select(a => a.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        return RolToEntityCode
            .Where(kv => codigos.Contains(kv.Value))
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    private static ParteRol? ParseRol(string? rol) => rol?.Trim().ToLowerInvariant() switch
    {
        "comprador" => ParteRol.Comprador,
        "vendedor" => ParteRol.Vendedor,
        "locatario" => ParteRol.Locatario,
        _ => null,
    };

    private static string RolToCode(ParteRol rol) => rol switch
    {
        ParteRol.Comprador => "comprador",
        ParteRol.Vendedor => "vendedor",
        // El literal que ya leen FurCommand, el resolver de destinatarios y el ciclo de vida.
        ParteRol.Locatario => "locatario",
        _ => rol.ToString().ToLowerInvariant(),
    };

    /// <remarks>
    /// ADR-0053 — ordenado por rol y luego por <c>Ordinal</c> ascendente dentro de cada rol (contrato
    /// OpenAPI: "ordenados por ordinal ascendente dentro de cada rol"), para que el principal
    /// (<c>ordinal=1</c>) siempre encabece la lista de su lado.
    /// </remarks>
    internal static ActorsResponse ToResponse(ProcedureInstance instance) =>
        new(instance.Actors
            .OrderBy(a => a.ActorType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Ordinal)
            .Select(a =>
            {
                var (ciudad, direccion, rl, mandante) = ActorMetadataReader.Parse(a.Metadata);
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
                    rl,
                    mandante,
                    a.Ordinal,
                    a.OwnershipPercentage);
            })
            .ToList());

    private static string SerializeMetadata(
        string? ciudad,
        string? direccion,
        ActorRepresentanteLegal? rl,
        ActorMandante? mandante = null) =>
        ActorMetadataReader.Serialize(ciudad, direccion, rl, mandante);

    /// <summary>
    /// Alias interno (HU #10955) — delega en <see cref="ActorMetadataReader.Parse"/>.
    /// </summary>
    internal static (string? Ciudad, string? Direccion, ActorRepresentanteLegal? RepresentanteLegal, ActorMandante? Mandante) ParseMetadata(string? metadata) =>
        ActorMetadataReader.Parse(metadata);
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

/// <summary>
/// Logging source-generated (CA1848) del disparador de validación de identidad (HU #11665).
/// <b>Sin PII (Ley 1581):</b> solo el id de la instancia, el rol de la parte y el código del motivo.
/// Ni correo, ni número de documento, ni nombre del representante.
/// </summary>
internal static partial class PutActorsLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se envió la validación de identidad a la parte {Parte} del trámite {InstanceId}: {Motivo}.")]
    public static partial void ValidacionNoEnviada(
        ILogger logger, Guid instanceId, string? parte, string motivo);
}
