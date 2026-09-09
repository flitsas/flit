using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// Dobles de los puertos de PRELLENADO (HU #12206, Feature #12201). A mano y no con NSubstitute
/// porque varios AC se juegan en <b>cuántas veces</b> se consultó cada fuente: la precedencia
/// «directorio primero, RUES solo si el directorio no responde» no se demuestra observando el
/// resultado, sino contando llamadas.
/// </summary>
internal sealed class FakeStandaloneVehiclePrefill : IStandaloneVehiclePrefill
{
    public int Calls { get; private set; }

    public string? LastPlate { get; private set; }

    /// <summary>Los 12 campos que el RUNT sí devuelve, con las claves canónicas del repo.</summary>
    public static IReadOnlyDictionary<string, string?> RuntCompleto =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["plate"] = "ABC123",
            ["vehicle_brand"] = "CHEVROLET",
            ["vehicle_line"] = "SPARK GT",
            ["vehicle_year"] = "2019",
            ["vehicle_class"] = "AUTOMOVIL",
            ["vehicle_body_type"] = "HATCHBACK",
            ["vehicle_color"] = "ROJO",
            ["vehicle_engine_number"] = "MOT-998877",
            ["vehicle_chassis"] = "CHS-112233",
            ["vin"] = "9GAJC1234K5678901",
            ["vehicle_series"] = "SER-445566",
            ["vehicle_service"] = "PARTICULAR",
            ["transit_office_name"] = "SECRETARIA DE MOVILIDAD DE ENVIGADO",
            // Ruido que el RUNT también manda y que el anexo NO pide: el mapeo debe ignorarlo.
            ["vehicle_fuel"] = "GASOLINA",
            ["runt_tiene_prendas"] = "SI",
        };

    public StandaloneVehicleLookupResult Result { get; set; } =
        new(true, RuntCompleto, "kyverum_runt", null);

    public Task<StandaloneVehicleLookupResult> LookupAsync(
        Guid tenantId,
        string plate,
        string? ownerDocumentType,
        string? ownerDocumentNumber,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastPlate = plate;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeStandaloneRuntPersonPrefill : IStandaloneRuntPersonPrefill
{
    public int Calls { get; private set; }

    public StandaloneRuntPersonResult Result { get; set; } =
        new(true, "MARIA CAMILA GOMEZ RUIZ", "MARIA CAMILA", "GOMEZ RUIZ", "kyverum_runt_conductor", null);

    public Task<StandaloneRuntPersonResult> LookupAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeStandaloneActorContactLookup : IStandaloneActorContactLookup
{
    public int Calls { get; private set; }

    public StandaloneContactLookupResult Result { get; set; } =
        new(true, "MEDELLIN", "CALLE 10 # 20-30", null);

    public Task<StandaloneContactLookupResult> LookupAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

/// <summary>
/// Directorio de representantes legales del tenant. Solo se implementa de verdad
/// <see cref="FindActiveByCompanyNitAsync"/> —lo único que consume el prellenado—; el resto lanza,
/// que es la forma de que una llamada inesperada aparezca como fallo y no como silencio.
/// </summary>
internal sealed class FakeLegalRepresentativeReader : ILegalRepresentativeReader
{
    public int Calls { get; private set; }

    /// <summary>Match del directorio. <c>null</c> = el NIT no está en el directorio.</summary>
    public LegalRepresentativeItem? Match { get; set; }

    /// <summary>Simula el directorio caído (no «sin match»): el handler debe caer al respaldo.</summary>
    public bool Caido { get; set; }

    public static LegalRepresentativeItem Representante(string nit) => new()
    {
        Id = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
        TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        CompanyDocumentNumber = nit,
        CompanyName = "TRANSPORTES DEL SUR SAS",
        DocumentType = "CC",
        DocumentNumber = "71234567",
        Name = "CARLOS",
        FirstLastName = "MEJIA",
        SecondLastName = "OSORIO",
        City = "ENVIGADO",
        IsActive = true,
        Companies =
        [
            new LegalRepresentativeCompanySummary(
                Guid.Parse("cccccccc-0000-0000-0000-000000000001"), nit, "TRANSPORTES DEL SUR SAS")
            {
                IsPrimary = true,
                City = "ENVIGADO",
            },
        ],
    };

    public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAsync(
        Guid tenantId, string companyNit, CancellationToken cancellationToken = default)
    {
        Calls++;

        if (Caido)
        {
            throw new InvalidOperationException("directorio no disponible");
        }

        return Task.FromResult(Match);
    }

    public Task<PagedResult<LegalRepresentativeItem>> ListPagedAsync(
        Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<LegalRepresentativeItem?> GetByIdAsync(
        Guid tenantId, Guid id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<LegalRepresentativeItem?> FindActiveByCompanyNitAndDocumentAsync(
        Guid tenantId, string companyNit, string documentType, string documentNumber,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<LegalRepresentativeItem>> ListActiveByCompanyNitAsync(
        Guid tenantId, string companyNit, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<LegalRepresentativeItem?> FindActiveByDocumentAsync(
        Guid tenantId, string documentType, string documentNumber, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<RepresentedCompanyItem>> ListRepresentedCompaniesAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<RepresentedCompanyItem?> FindRepresentedCompanyByNitAsync(
        Guid tenantId, string documentNumber, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<RepresentedCompanyItem?> FindActiveCompanyForRepresentativeAsync(
        Guid tenantId, Guid representativeId, string documentNumber, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, LegalRepresentativeBrief>> FindBriefByIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}
