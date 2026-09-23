using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12775 — un requisito de Cámara de Comercio por actor persona jurídica del trámite.
/// <para><c>exencion</c> viaja como texto (<c>ninguna</c> | <c>firma_precargada</c> |
/// <c>escritura_vigente</c>) y no como número: es lo que el paso del actor usa para elegir el texto
/// que lee el gestor, y un entero en el JSON obligaría al cliente a mantener su propia tabla de
/// equivalencias.</para>
/// </summary>
public sealed record CamaraComercioRequirementDto(
    string Rol,
    string Tipo,
    bool EsObligatorio,
    string Exencion,
    // HU #12776 — vigencia del certificado ya cargado: `vigente` | `excedida` | `indeterminada`.
    // `indeterminada` es el caso en que el OCR no pudo leer la fecha, y NO se pinta alerta: una
    // fecha ilegible no es un documento vencido.
    string Vigencia,
    int? DiasDesdeExpedicion);

public sealed record CamaraComercioRequirementsResponse(
    IReadOnlyList<CamaraComercioRequirementDto> Requirements);

/// <summary>
/// HU #12775 — expone la escalera de obligatoriedad del certificado de Cámara de Comercio para los
/// actores del trámite. Lo consume el paso del actor en el asistente (HU #12777).
/// </summary>
public sealed class GetCamaraComercioRequirementsHandler(
    IProcedureInstanceRepository repo,
    CamaraComercioRequirementResolver resolver,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public Task<(CamaraComercioRequirementsResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default) =>
        ResolveAsync(id, tenantId, borrador: null, ct);

    /// <summary>
    /// HU #12777 AC1 / HU #12779 — la misma escalera, pero sobre los actores que el gestor tiene en
    /// pantalla y todavía no guardó. Los actores solo se persisten con «Continuar y guardar», así que
    /// resolver sobre lo guardado dejaba el buzón un paso atrás: no aparecía al marcar NIT y no se iba
    /// al volver a persona natural. No persiste nada.
    /// </summary>
    public Task<(CamaraComercioRequirementsResponse? Result, string? Error)> HandlePreviewAsync(
        Guid id,
        Guid tenantId,
        IReadOnlyList<ActorInput> borrador,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(borrador);
        return ResolveAsync(id, tenantId, borrador, ct);
    }

    private async Task<(CamaraComercioRequirementsResponse? Result, string? Error)> ResolveAsync(
        Guid id,
        Guid tenantId,
        IReadOnlyList<ActorInput>? borrador,
        CancellationToken ct)
    {
        // WithDetails y no WithActors: la vigencia se calcula sobre la fecha que el OCR dejó en
        // field_values al cargar el certificado.
        var instance = await repo.GetByIdWithDetailsAsync(id, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return (null, "not_found");

        var actors = borrador is null
            ? instance.Actors
            : [.. borrador.Select(a => ActorTransitorio(tenantId, instance.Id, a))];

        var requirements = await resolver
            .ResolveAsync(tenantId, actors, ct)
            .ConfigureAwait(false);

        // Día calendario de Colombia (UTC-5, sin DST — ADR-0025 §3): a las 7 p. m. de Bogotá ya es el
        // día siguiente en UTC, y contar con esa fecha envejecería el certificado medio día antes.
        var hoy = DateOnly.FromDateTime(
            _time.GetUtcNow().ToOffset(ColombiaTime.Offset).DateTime);

        return (new CamaraComercioRequirementsResponse(
            [.. requirements.Select(r =>
            {
                var expedicion = instance.FieldValues
                    .FirstOrDefault(f => string.Equals(
                        f.FieldKey, CamaraComercioFieldKeys.Expedicion(r.Rol), StringComparison.OrdinalIgnoreCase))
                    ?.ValueText;
                var vigencia = CamaraComercioVigencia.EvaluarTexto(expedicion, hoy);

                return new CamaraComercioRequirementDto(
                    r.Rol,
                    r.Tipo,
                    r.EsObligatorio,
                    ToWire(r.Exencion),
                    CamaraComercioVigencia.ToWire(vigencia.Estado),
                    vigencia.Dias);
            })]), null);
    }

    /// <summary>
    /// Nombre estable de la exención en el contrato. Se escribe a mano en vez de derivarlo del enum
    /// con <c>ToString()</c>: renombrar un valor del enum es refactor interno y no puede cambiar en
    /// silencio lo que el cliente ya interpreta.
    /// </summary>
    /// <summary>
    /// Actor en memoria con lo único que la escalera lee: rol, documento y el representante legal de
    /// la metadata (sujeto del baúl de firmas). Nunca se agrega a la instancia ni al contexto de EF.
    /// </summary>
    private static ProcedureInstanceActor ActorTransitorio(Guid tenantId, Guid instanceId, ActorInput a) =>
        new()
        {
            TenantId = tenantId,
            ProcedureInstanceId = instanceId,
            ActorType = (a.Rol ?? string.Empty).Trim().ToLowerInvariant(),
            DocumentType = (a.TipoDocumento ?? string.Empty).Trim().ToUpperInvariant(),
            DocumentNumber = (a.NumeroDocumento ?? string.Empty).Trim(),
            FullName = a.NombreCompleto ?? string.Empty,
            PersonType = ActorPersonTypes.ResolveForDocument(a.TipoDocumento, a.PersonType),
            EsRepresentanteLegal = a.EsRepresentanteLegal,
            Metadata = ActorMetadataReader.Serialize(a.Ciudad, a.Direccion, a.RepresentanteLegal, a.Mandante),
            Ordinal = a.Ordinal,
        };

    public static string ToWire(CamaraComercioExencion exencion) => exencion switch
    {
        CamaraComercioExencion.FirmaPrecargada => "firma_precargada",
        CamaraComercioExencion.EscrituraVigente => "escritura_vigente",
        _ => "ninguna",
    };
}
