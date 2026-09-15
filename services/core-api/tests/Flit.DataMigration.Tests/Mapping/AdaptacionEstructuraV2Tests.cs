using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Mapping;

/// <summary>
/// Revisión del migrador contra el V2 de septiembre de 2026. Cada prueba fija una columna o regla
/// que V2 añadió DESPUÉS de escribir el migrador y que, sin estas adaptaciones, dejaba el trámite
/// migrado técnicamente correcto pero invisible o incompleto para quien lo opera:
/// <list type="bullet">
///   <item><c>TransitOfficeId</c> + <c>transit_office_id</c>: sin ellos el organismo no ve el trámite
///   en su bandeja (HU #11945) aunque el código esté como texto.</item>
///   <item>Copropietarios con ordinal y porcentaje (ADR-0053): antes se descartaban con un aviso.</item>
///   <item><c>revocado</c> (HU #12165): antes Revoked de V1 se colapsaba en 'anulado'.</item>
/// </list>
/// </summary>
public sealed class AdaptacionEstructuraV2Tests
{
    private static readonly Guid Tenant = Guid.Parse("0ad1c0de-0000-4000-8000-000000000001");

    private static readonly TransitOfficeRef Funza = new(
        Guid.Parse("eeacc872-a522-56bb-9150-70776b094009"), "25286000", "STRIA TTOyTTE MCPAL FUNZA",
        "25286", "FUNZA", IsActive: true);

    private static MappingContext Contexto(TransitOfficeRef? organismo = null) => new()
    {
        TenantId = Tenant,
        ProcedureTypeId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        SystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        OwnerEntityId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        BuyerEntityId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
        TransitOffice = organismo,
    };

    private static V1SourceRecord Matricula(
        int estado = 7,
        Dictionary<string, string?>? columnas = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? copropietarios = null)
    {
        var cols = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["traffic_secretary_code"] = "25286000",
            ["traffic_secretary_name"] = "STRIA TTOyTTE MCPAL FUNZA",
            ["traffic_secretary_city"] = "FUNZA",
            ["vehicle_owner_document_type"] = "C",
            ["vehicle_owner_document_number"] = "1152191826",
            ["vehicle_owner_name"] = "DANIELA",
            ["vehicle_owner_first_last_name"] = "HOYOS",
        };
        foreach (var (k, v) in columnas ?? [])
        {
            cols[k] = v;
        }

        return new V1SourceRecord
        {
            Id = 29890,
            SourceTable = "vehicle_registration_master",
            ProcessStatus = estado,
            Columns = cols,
            StatusHistory = [],
            CoOwners = copropietarios ?? [],
        };
    }

    private static string? Campo(MappedProcedure m, string clave) =>
        m.FieldValues.SingleOrDefault(f => f.FieldKey == clave)?.ValueText;

    // ------------------------------------------------------------- organismo de tránsito

    [Fact]
    public void ConOrganismoResueltoFijaElIdYLasClavesQueLeeElFlujoNativo()
    {
        var m = RegistrationMapper.Map(Matricula(), Contexto(Funza));

        m.Instance.TransitOfficeId.Should().Be(Funza.Id);
        Campo(m, TransitOfficeFieldKeys.Id).Should().Be(Funza.Id.ToString());
        Campo(m, TransitOfficeFieldKeys.Code).Should().Be("25286000");
        Campo(m, TransitOfficeFieldKeys.Name).Should().Be("STRIA TTOyTTE MCPAL FUNZA");
        // V2 guarda el CÓDIGO de ciudad en transit_office_city y el nombre aparte.
        Campo(m, TransitOfficeFieldKeys.City).Should().Be("25286");
        Campo(m, TransitOfficeFieldKeys.CityName).Should().Be("FUNZA");
        m.Warnings.Should().NotContain(w => w.Contains("no existe en catalogs.transit_offices"));
    }

    [Fact]
    public void SinOrganismoResueltoConservaElTextoDeV1YAvisaQueNoSaldraEnLaBandeja()
    {
        var m = RegistrationMapper.Map(Matricula(), Contexto(organismo: null));

        m.Instance.TransitOfficeId.Should().BeNull();
        Campo(m, TransitOfficeFieldKeys.Id).Should().BeNull();
        Campo(m, TransitOfficeFieldKeys.Code).Should().Be("25286000");
        Campo(m, TransitOfficeFieldKeys.City).Should().Be("FUNZA", "sin catálogo se respeta el texto de V1");
        m.Warnings.Should().ContainSingle(w => w.Contains("25286000") && w.Contains("bandeja"));
    }

    [Fact]
    public void ElTraspasoTambienResuelveElOrganismo()
    {
        var registro = new V1SourceRecord
        {
            Id = 8617,
            SourceTable = "vehicle_transfer_master",
            ProcessStatus = 6,
            Columns = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["traffic_secretary_code"] = "25286000",
                ["traffic_secretary_city"] = "FUNZA",
            },
            StatusHistory = [],
        };

        var m = TransferMapper.Map(registro, Contexto(Funza));

        m.Instance.TransitOfficeId.Should().Be(Funza.Id);
        Campo(m, TransitOfficeFieldKeys.Id).Should().Be(Funza.Id.ToString());
        // Cada clave una sola vez: el id del field_value es determinístico por clave y una
        // duplicada reventaría la PK al cargar.
        m.FieldValues.Select(f => f.FieldKey).Should().OnlyHaveUniqueItems();
    }

    // ------------------------------------------------------------- copropietarios

    private static IReadOnlyDictionary<string, string?> Copropietario(
        string id, string documento, string nombre, string apellido, bool solidario, string porcentaje) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["document_type"] = "C",
            ["document_number"] = documento,
            ["name"] = nombre,
            ["first_last_name"] = apellido,
            ["is_solidarity_buyer"] = solidario ? "true" : "false",
            ["ownership_percentage"] = porcentaje,
            ["city"] = "MEDELLIN",
            ["email"] = $"{nombre.ToLowerInvariant()}@example.com",
        };

    [Fact]
    public void MultipropietarioMigraAlTitularConSuPorcentajeYALosCopropietariosConOrdinal()
    {
        var registro = Matricula(
            columnas: new() { ["has_multiple_owners"] = "true" },
            copropietarios:
            [
                // V1 repite al titular del master con is_solidarity_buyer y documento con espacio.
                Copropietario("813", "1152191826", "DANIELA", "HOYOS", solidario: true, "50.00"),
                Copropietario("814", " 32143812", "CAROLINA", "OSPINA", solidario: false, "50.00"),
            ]);

        var m = RegistrationMapper.Map(registro, Contexto());

        m.Actors.Should().HaveCount(2);

        var titular = m.Actors.Single(a => a.Ordinal == 1);
        titular.DocumentNumber.Should().Be("1152191826");
        titular.OwnershipPercentage.Should().Be(50.00m);

        var copropietaria = m.Actors.Single(a => a.Ordinal == 2);
        copropietaria.ActorType.Should().Be(titular.ActorType);
        copropietaria.ProcedureEntityId.Should().Be(titular.ProcedureEntityId);
        copropietaria.DocumentNumber.Should().Be("32143812", "V1 guarda el documento con espacios");
        copropietaria.FullName.Should().Be("CAROLINA OSPINA");
        copropietaria.OwnershipPercentage.Should().Be(50.00m);
        copropietaria.Email.Should().Be("carolina@example.com");
        copropietaria.Metadata.Should().Contain("\"legacy_v1_actor_id\":\"814\"");
        copropietaria.Id.Should().NotBe(titular.Id);

        m.Warnings.Should().ContainSingle(w => w.Contains("1 copropietario(s)"));
        m.Warnings.Should().NotContain(w => w.Contains("pendiente de definir"));
    }

    [Fact]
    public void ElTitularSeReconocePorDocumentoAunqueV1NoLoMarqueSolidario()
    {
        var registro = Matricula(
            columnas: new() { ["has_multiple_owners"] = "true" },
            copropietarios:
            [
                Copropietario("1", "1152191826", "DANIELA", "HOYOS", solidario: false, "70"),
                Copropietario("2", "900", "PEPE", "PEREZ", solidario: false, "30"),
            ]);

        var m = RegistrationMapper.Map(registro, Contexto());

        m.Actors.Should().HaveCount(2, "el titular no se duplica como copropietario");
        m.Actors.Single(a => a.Ordinal == 1).OwnershipPercentage.Should().Be(70m);
        m.Actors.Single(a => a.Ordinal == 2).DocumentNumber.Should().Be("900");
    }

    [Fact]
    public void MasDeCuatroPorRolSeDescartanConAviso()
    {
        var filas = Enumerable.Range(1, 6)
            .Select(i => Copropietario(i.ToString(), $"10{i}", $"P{i}", "X", solidario: false, "10"))
            .ToList();

        var m = RegistrationMapper.Map(
            Matricula(columnas: new() { ["has_multiple_owners"] = "true" }, copropietarios: filas),
            Contexto());

        // Titular (1) + 3 agregados (2..4); los otros 3 fuera.
        m.Actors.Should().HaveCount(4);
        m.Actors.Max(a => a.Ordinal).Should().Be(4);
        m.Warnings.Count(w => w.Contains("máximo 4")).Should().Be(3);
    }

    [Fact]
    public void MultipropietarioSinFilasEnV1SoloAvisa()
    {
        var m = RegistrationMapper.Map(
            Matricula(columnas: new() { ["has_multiple_owners"] = "true" }), Contexto());

        m.Actors.Should().HaveCount(1);
        m.Warnings.Should().ContainSingle(w => w.Contains("no trae filas"));
    }

    // ------------------------------------------------------------- estados

    [Fact]
    public void RevokedDeV1EsRevocadoEnV2YYaNoEsAmbiguo()
    {
        var m = RegistrationMapper.Map(Matricula(estado: 9), Contexto());

        m.FinalStatus.Should().Be(TramiteEstado.Revocado);
        TramiteEstado.EsFinal(m.FinalStatus).Should().BeTrue();
        RegistrationStateMap.Instance.IsAmbiguous(9).Should().BeFalse();
        m.Warnings.Should().NotContain(w => w.Contains("no tiene equivalente exacto"));
    }
}
