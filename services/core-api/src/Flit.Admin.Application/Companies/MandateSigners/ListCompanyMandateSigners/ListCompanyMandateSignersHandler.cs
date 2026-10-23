using Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Queries.Domain.Time;

namespace Flit.Admin.Application.Companies.MandateSigners.ListCompanyMandateSigners;

/// <summary>Organismo de tránsito ofrecido a la compañía al elegir dónde aplica un mandatario.</summary>
public sealed record CompanyTransitOfficeResponse(
    Guid TransitOfficeId,
    string Code,
    string Name,
    string FormatName = "");

/// <summary>
/// HU #11202 — mandatarios vistos desde la COMPAÑÍA gestora. Es la vista inversa de la consola del
/// organismo: desde este cambio el alta la hace la empresa y marca en qué organismos aplica, no al
/// revés.
/// </summary>
public sealed class ListCompanyMandateSignersHandler
{
    private readonly IMandateSignerReader _reader;
    private readonly IManagingCompanyDirectory? _companies;

    public ListCompanyMandateSignersHandler(IMandateSignerReader reader, IManagingCompanyDirectory? companies = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _companies = companies;
    }

    public Task<IReadOnlyList<MandateSignerResponse>> HandleAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default) =>
        HandleAsync(companyTenantId, MandateSignerActorKind.None, cancellationToken);

    /// <summary>
    /// HU #13134 — con el rol del actor, cada fila trae <c>origin</c> y las banderas <c>puedeEditar</c> /
    /// <c>puedeEliminar</c> calculadas con la regla única por origen y rol.
    /// </summary>
    public async Task<IReadOnlyList<MandateSignerResponse>> HandleAsync(
        Guid companyTenantId,
        MandateSignerActorKind actor,
        CancellationToken cancellationToken = default)
    {
        var signers = await _reader
            .ListByCompanyAsync(companyTenantId, cancellationToken).ConfigureAwait(false);

        // HU #13179b — id, nombre y NIT de las asociadas, en una sola consulta (sin N+1).
        var officeCompanies = await AssociatedCompaniesEnricher
            .EnrichAsync(signers, _companies, cancellationToken).ConfigureAwait(false);

        var today = ColombiaTime.Today(TimeProvider.System);

        return
        [
            .. signers.Select(s => new MandateSignerResponse(
                s.Id,
                s.TransitOfficeId,
                s.FullName,
                s.DocumentType,
                s.DocumentNumber,
                s.IntegrityHash,
                s.Email,
                s.UserId,
                s.SignatureVaultId,
                s.IdentityStatus,
                s.RegisteredAt,
                s.IsActive,
                s.CompanyTenantIds,
                s.TransitOfficeIds,
                s.PhysicalSignatureOfficeIds,
                officeCompanies[s.Id],
                s.SignerModel,
                s.SignatureMethod,
                s.ValidityKind,
                s.ValidFrom,
                s.ValidTo,
                s.ValidityStatusOn(today),
                s.FirmaValidezOn(today)?.Valida,
                s.FirmaValidezOn(today)?.Motivo,
                s.Origin,
                MandateSignerOriginRules.CanModify(actor, s.Origin),
                MandateSignerOriginRules.CanModify(actor, s.Origin))),
        ];
    }
}

/// <summary>
/// HU #11202 (AC2) — organismos que la compañía puede elegir. Solo los que tiene habilitados: registrar
/// un mandatario en un organismo donde no puede radicar no serviría de nada.
/// </summary>
public sealed class ListCompanyTransitOfficesHandler
{
    private readonly IMandateSignerReader _reader;

    public ListCompanyTransitOfficesHandler(IMandateSignerReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<IReadOnlyList<CompanyTransitOfficeResponse>> HandleAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default)
    {
        var options = await _reader
            .ListCompanyTransitOfficesAsync(companyTenantId, cancellationToken).ConfigureAwait(false);

        return [.. options.Select(o => new CompanyTransitOfficeResponse(o.TransitOfficeId, o.Code, o.Name, o.FormatName))];
    }
}
