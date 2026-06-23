using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Contrato congelado consumido por el frontend (Slice 2).
/// <c>rol</c>: comprador|vendedor; <c>tipoDocumento</c>: CC|CE|NIT|PAS|TI.
/// <c>ciudad</c>/<c>direccion</c> son opcionales y se persisten en <c>actor.metadata</c> (JSON).
/// </summary>
public sealed record ActorInput(
    string Rol,
    string TipoDocumento,
    string NumeroDocumento,
    string NombreCompleto,
    string Email,
    string? Telefono,
    string? Ciudad = null,
    string? Direccion = null);

public sealed record ActorDto(
    string Rol,
    string TipoDocumento,
    string NumeroDocumento,
    string NombreCompleto,
    string Email,
    string? Telefono,
    string? Ciudad = null,
    string? Direccion = null);

public sealed record PutActorsRequest(IReadOnlyList<ActorInput> Actors);

public sealed record ActorsResponse(IReadOnlyList<ActorDto> Actors);

/// <summary>
/// Captura de actores end-to-end. El rol vive en <c>actor_type</c>; cada rol se mapea a
/// una <c>procedure_entity</c> seedeada (HU10151): comprador→BUYER, vendedor→OWNER. Esto
/// respeta el UNIQUE(procedure_instance_id, procedure_entity_id): cada rol aparece a lo sumo
/// una vez por instancia. Los roles permitidos salen de la matriz de dominio según
/// <c>modalidad_entrada</c> (matricula_inicial→comprador; traspaso→vendedor+comprador).
/// </summary>
public sealed class PutActorsHandler(
    IProcedureInstanceRepository repo,
    ICatalogRepository catalogRepo)
{
    // Documentos válidos del contrato congelado (front consume el mismo set).
    private static readonly HashSet<string> ValidDocumentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "CC", "CE", "NIT", "PAS", "TI" };

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
        var instance = await repo.GetByIdWithActorsAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Status != ProcedureInstanceStatus.Draft)
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
            if (string.IsNullOrWhiteSpace(a.NombreCompleto))
                return (null, "missing_full_name");
            if (string.IsNullOrWhiteSpace(a.Email) || !TramiteDocumento.EmailValido(a.Email))
                return (null, "invalid_email");
        }

        // 2. Roles permitidos según modalidad_entrada (matriz de dominio, no hardcode).
        var journey = ResolveJourney(instance);
        var allowedRoles = journey is null
            ? new HashSet<ParteRol>()
            : journey.Partes.Select(p => p.Rol).ToHashSet();
        var requiredRoles = journey is null
            ? new HashSet<ParteRol>()
            : journey.Partes.Where(p => p.Obligatorio).Select(p => p.Rol).ToHashSet();

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

        // 3. Partes obligatorias presentes (set completo, reemplazo total).
        if (!requiredRoles.IsSubsetOf(providedRoles))
            return (null, "missing_required_rol");

        // 4. Unicidad vendedor ≠ comprador (documento y email) vía dominio.
        var error = ValidateTraspasoPartes(inputs);
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

        // 6. Reemplazo total del set.
        // Instancia trackeada (GetByIdWithDetailsAsync sin AsNoTracking): el change tracker ve los
        // actores quitados como Deleted y los nuevos como Added. NO se llama Update() (marcaría los
        // hijos nuevos como Modified → UPDATE de 0 filas en vez de INSERT).
        //
        // UNIQUE(procedure_instance_id, procedure_entity_id): un re-PUT puede reusar la misma
        // procedure_entity_id (p.ej. comprador→BUYER) que un actor recién borrado. EF Core NO
        // garantiza orden DELETE-antes-de-INSERT para la MISMA tabla dentro de un SaveChanges, así
        // que un solo SaveChanges arriesga violar el UNIQUE. Por eso se hace un SaveChanges
        // intermedio: primero persiste los DELETE del Clear(), luego los INSERT de los nuevos.
        instance.Actors.Clear();
        await repo.SaveChangesAsync(ct);

        var now = DateTimeOffset.UtcNow;
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
                Metadata = SerializeMetadata(a.Ciudad, a.Direccion),
                CreatedAt = now,
            };
            instance.Actors.Add(actor);
            // PK store-generated (uuidv7) con Id ya seteado: marcar Added explícito para forzar
            // INSERT. Sin esto, EF infiere Modified por la PK no-default → UPDATE de 0 filas.
            repo.Add(actor);
        }

        await repo.SaveChangesAsync(ct);

        return (ToResponse(instance), null);
    }

    private static string? ValidateTraspasoPartes(IReadOnlyList<ActorInput> inputs)
    {
        var vendedorInput = inputs.FirstOrDefault(a => ParseRol(a.Rol) == ParteRol.Vendedor);
        var compradorInput = inputs.FirstOrDefault(a => ParseRol(a.Rol) == ParteRol.Comprador);
        if (vendedorInput is null || compradorInput is null)
            return null;

        var vendedor = new ParteDatos(vendedorInput.NombreCompleto, vendedorInput.NumeroDocumento, vendedorInput.Email);
        var comprador = new ParteDatos(compradorInput.NombreCompleto, compradorInput.NumeroDocumento, compradorInput.Email);
        var dup = TraspasoPartes.DetectarDuplicadas(vendedor, comprador);
        return TraspasoPartes.MensajeDuplicadas(dup) is null ? null : "partes_duplicadas";
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
                var (ciudad, direccion) = ParseMetadata(a.Metadata);
                return new ActorDto(
                    a.ActorType,
                    a.DocumentType,
                    a.DocumentNumber,
                    a.FullName,
                    a.Email ?? string.Empty,
                    a.Phone,
                    ciudad,
                    direccion);
            })
            .ToList());

    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    /// <summary>Serializa ciudad/dirección al JSON de <c>actor.metadata</c>. Vacíos → "{}".</summary>
    private static string SerializeMetadata(string? ciudad, string? direccion)
    {
        var c = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim();
        var d = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();
        return c is null && d is null
            ? "{}"
            : JsonSerializer.Serialize(new ActorMetadata(c, d), MetadataJson);
    }

    /// <summary>Lee ciudad/dirección de <c>actor.metadata</c>. Robusto ante null/"{}"/JSON inválido.</summary>
    private static (string? Ciudad, string? Direccion) ParseMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata) || metadata == "{}")
            return (null, null);
        try
        {
            var m = JsonSerializer.Deserialize<ActorMetadata>(metadata, MetadataJson);
            return (m?.Ciudad, m?.Direccion);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private sealed record ActorMetadata(string? Ciudad, string? Direccion);
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
