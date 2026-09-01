using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Services;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

public sealed record PreviewFurRequest(
    Guid? ProcedureTypeId,
    string? SellerPersonKind,
    string? BuyerPersonKind,
    string? VehicleKind,
    bool? CambioColor = null,
    bool? CambioCombustible = null,
    bool? CambioCarroceria = null,
    bool? Blindaje = null,
    string? Prenda = null,
    bool? FillAll = null,
    string? VehicleClass = null,
    string? TemplateFormat = null);

public enum PreviewFurStatus
{
    Ok,
    BadRequest,
    NotFound,
}

public sealed record PreviewFurResult(
    PreviewFurStatus Status,
    string? Error,
    GeneratedDocument? Document,
    IReadOnlyList<string>? Allowed);

/// <summary>HU #11701 — preview sintético del FUR sin persistir adjuntos.</summary>
public sealed class PreviewFurHandler(
    IProcedureTypeRepository procedureTypes,
    IFurDocumentGenerator generator,
    IFurTemplateResolver templateResolver)
{
    public static readonly string[] AllowedVehicleKinds = ["carro", "moto", "camioneta", "remolque", "maquinaria"];
    public static readonly string[] AllowedPersonKinds = ["natural", "juridica"];
    public static readonly string[] AllowedTemplateFormats = ["AUTOMOTOR", "MAQUINARIA", "REMOLQUES"];

    public async Task<PreviewFurResult> HandleAsync(PreviewFurRequest request, CancellationToken ct = default)
    {
        if (request.FillAll == true)
        {
            var format = FurTemplateFormat.Automotor;
            if (!string.IsNullOrWhiteSpace(request.TemplateFormat))
            {
                if (!FurTemplateResolution.TryParseFormat(request.TemplateFormat, out format))
                    return new PreviewFurResult(PreviewFurStatus.BadRequest, "template_format_invalido", null, AllowedTemplateFormats);
            }
            else if (!string.IsNullOrWhiteSpace(request.VehicleKind))
            {
                if (!FurPreviewSample.TryParseVehicleKind(request.VehicleKind, out var fillKind))
                    return new PreviewFurResult(PreviewFurStatus.BadRequest, "vehicle_kind_invalido", null, AllowedVehicleKinds);
                format = FurPreviewSample.TemplateFormatFor(fillKind);
            }

            var fillAll = generator.GenerateFurFillAll(format);
            return new PreviewFurResult(PreviewFurStatus.Ok, null, fillAll, null);
        }

        if (request.ProcedureTypeId is null || request.ProcedureTypeId == Guid.Empty)
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "procedure_type_id_requerido", null, null);

        if (!FurPreviewSample.TryParsePersonKind(request.SellerPersonKind, out var sellerKind)
            || !FurPreviewSample.TryParsePersonKind(request.BuyerPersonKind, out var buyerKind))
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "person_kind_invalido", null, AllowedPersonKinds);

        if (!FurPreviewSample.TryParsePrenda(request.Prenda, out var prenda))
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "prenda_invalida", null, ["ninguna", "inscripcion", "levantamiento", "ambas"]);

        // HU #11701 — vehicle_kind se valida ANTES del lookup del tipo: un "barco" con un Guid
        // inexistente es 400, no 404. vehicle_class (catálogo) sustituye a vehicle_kind.
        string? vehicleKind = null;
        if (string.IsNullOrWhiteSpace(request.VehicleClass)
            && !FurPreviewSample.TryParseVehicleKind(request.VehicleKind, out vehicleKind))
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "vehicle_kind_invalido", null, AllowedVehicleKinds);

        var type = await procedureTypes.GetByIdAsync(request.ProcedureTypeId.Value, ct).ConfigureAwait(false);
        if (type is null)
            return new PreviewFurResult(PreviewFurStatus.NotFound, "procedure_type_no_encontrado", null, null);

        // ADR-0051 — la preview deriva del gate_profile REAL del tipo, no de una heurística de código/
        // familia: unifica el simulador con FurCommand para que dejen de contradecirse.
        var profile = ProcedureTypeGateProfile.FromJson(type.GateProfile);
        var flags = new FurPreviewFlags(
            request.CambioColor,
            request.CambioCombustible,
            request.CambioCarroceria,
            request.Blindaje,
            prenda);

        FurDocumentData data;
        if (!string.IsNullOrWhiteSpace(request.VehicleClass))
        {
            var match = await templateResolver.ResolveMatchAsync(request.VehicleClass, ct).ConfigureAwait(false);
            data = FurPreviewSample.BuildFromClassification(
                type.Code,
                type.Family,
                sellerKind,
                buyerKind,
                request.VehicleClass,
                match.Format,
                match.FieldToFill,
                flags,
                profile);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(vehicleKind);
            data = FurPreviewSample.Build(
                type.Code,
                type.Family,
                sellerKind,
                buyerKind,
                vehicleKind,
                flags,
                profile);
        }

        var doc = generator.GenerateFur(data);
        return new PreviewFurResult(PreviewFurStatus.Ok, null, doc, null);
    }
}

public sealed class ListFurClassificationsHandler(IFurTemplateResolver templateResolver)
{
    public Task<IReadOnlyList<FurClassificationCatalogItem>> HandleAsync(CancellationToken ct = default) =>
        templateResolver.ListCatalogAsync(ct);
}
