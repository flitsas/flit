using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Repositories;

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
    string? Prenda = null);

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
    IFurDocumentGenerator generator)
{
    public static readonly string[] AllowedVehicleKinds = ["carro", "moto", "remolque", "maquinaria"];
    public static readonly string[] AllowedPersonKinds = ["natural", "juridica"];

    public async Task<PreviewFurResult> HandleAsync(PreviewFurRequest request, CancellationToken ct = default)
    {
        if (request.ProcedureTypeId is null || request.ProcedureTypeId == Guid.Empty)
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "procedure_type_id_requerido", null, null);

        if (!FurPreviewSample.TryParseVehicleKind(request.VehicleKind, out var vehicleKind))
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "vehicle_kind_invalido", null, AllowedVehicleKinds);

        if (!FurPreviewSample.TryParsePersonKind(request.SellerPersonKind, out var sellerKind)
            || !FurPreviewSample.TryParsePersonKind(request.BuyerPersonKind, out var buyerKind))
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "person_kind_invalido", null, AllowedPersonKinds);

        if (!FurPreviewSample.TryParsePrenda(request.Prenda, out var prenda))
            return new PreviewFurResult(PreviewFurStatus.BadRequest, "prenda_invalida", null, ["ninguna", "inscripcion", "levantamiento", "ambas"]);

        var type = await procedureTypes.GetByIdAsync(request.ProcedureTypeId.Value, ct).ConfigureAwait(false);
        if (type is null)
            return new PreviewFurResult(PreviewFurStatus.NotFound, "procedure_type_no_encontrado", null, null);

        var data = FurPreviewSample.Build(
            type.Code,
            type.Family,
            sellerKind,
            buyerKind,
            vehicleKind,
            new FurPreviewFlags(
                request.CambioColor,
                request.CambioCombustible,
                request.CambioCarroceria,
                request.Blindaje,
                prenda));
        var doc = generator.GenerateFur(data);
        return new PreviewFurResult(PreviewFurStatus.Ok, null, doc, null);
    }
}
