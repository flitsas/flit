using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Mapping;

/// <summary>
/// HU #13072 — la prenda y las transformaciones que V1 declaró llegan al modelo de V2.
///
/// <para>
/// Antes quedaban solo en <c>legacy_v1_extras</c>: el dato no se perdía, pero V2 no lo lee de ahí, y
/// el listado, los filtros y Consultas decían «sin prenda» y «sin transformación». Cada prueba
/// verifica la marca con las MISMAS funciones que usa V2 (<see cref="TramiteMarcas"/>), no con una
/// réplica: si V2 cambia la regla, estas pruebas lo notan.
/// </para>
/// </summary>
public sealed class PrendaYTransformacionTests
{
    private static readonly MappingContext Contexto = new()
    {
        TenantId = Guid.Parse("0ad1c0de-0000-4000-8000-000000000001"),
        ProcedureTypeId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        SystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        OwnerEntityId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        BuyerEntityId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
    };

    private static V1SourceRecord Registro(string tabla, params (string Columna, string? Valor)[] columnas)
    {
        var cols = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["vehicle_owner_document_type"] = "C",
            ["vehicle_owner_document_number"] = "1152191826",
            ["vehicle_owner_name"] = "DANIELA",
        };
        foreach (var (columna, valor) in columnas)
        {
            cols[columna] = valor;
        }

        return new V1SourceRecord
        {
            Id = 4242,
            SourceTable = tabla,
            ProcessStatus = 6,
            Columns = cols,
            StatusHistory = [],
        };
    }

    private static MappedProcedure Matricula(params (string, string?)[] columnas) =>
        RegistrationMapper.Map(Registro("vehicle_registration_master", columnas), Contexto);

    private static MappedProcedure Traspaso(params (string, string?)[] columnas) =>
        TransferMapper.Map(Registro("vehicle_transfer_master", columnas), Contexto);

    private static string? Campo(MappedProcedure m, string clave) =>
        m.FieldValues.SingleOrDefault(f => f.FieldKey == clave)?.ValueText;

    private static bool MarcaTransformacion(MappedProcedure m) =>
        TramiteMarcas.TieneTransformacion(
            m.FieldValues.Where(f => f.ValueText is not null).ToDictionary(f => f.FieldKey, f => f.ValueText),
            "MATRICULA_NUEVA");

    /// <summary>Misma regla que ProcedureInstanceFiltroSql.TienePrenda: vigente y ni omitir ni sin_prenda.</summary>
    private static bool MarcaPrenda(MappedProcedure m) =>
        TramiteMarcas.TienePrenda(
            m.Prendas.Any(p => p.Estado == PrendaEstado.Vigente
                && p.Decision != PrendaDecision.SinPrenda
                && p.Decision != PrendaDecision.Omitir),
            "TRASPASO_STANDARD");

    // ------------------------------------------------------------- AC1 — color, carrocería, combustible

    [Fact]
    public void CambioDeColorPoneElOriginalComoRuntYElNuevoComoEfectivo()
    {
        var m = Matricula(
            ("vehicle_colors", "BLANCO NIEBLA"),
            ("switch_vehicle_color", "true"),
            ("new_vehicle_color", "ROJO COCA COLA"));

        Campo(m, "cambio_color").Should().Be("true");
        Campo(m, "vehicle_color_runt").Should().Be("BLANCO NIEBLA");
        Campo(m, "vehicle_color").Should().Be("ROJO COCA COLA");
        MarcaTransformacion(m).Should().BeTrue();
    }

    [Fact]
    public void CambioDeCombustibleConservaElCodigoDeV1TraducidoEnElRunt()
    {
        var m = Traspaso(
            ("vehicle_fuel_type", "1"),
            ("switch_vehicle_fuel_type", "true"),
            ("new_vehicle_fuel_type", "GNV"));

        Campo(m, "cambio_combustible").Should().Be("true");
        Campo(m, "vehicle_fuel_runt").Should().Be(VehicleCodeMap.DecodeFuel("1", out _));
        Campo(m, "vehicle_fuel").Should().Be("GNV");
    }

    [Fact]
    public void CambioDeCarroceriaSinValorNuevoSoloMarcaYNoTocaElDato()
    {
        var m = Matricula(
            ("vehicle_bodywork_type", "WAGON"),
            ("switch_vehicle_bodywork", "true"),
            ("new_vehicle_bodywork", null));

        Campo(m, "cambio_carroceria").Should().Be("true");
        Campo(m, "vehicle_body_type").Should().Be("WAGON");
        Campo(m, "vehicle_body_type_runt").Should().BeNull("sin valor nuevo no hay diff que declarar");
        m.FieldValues.Select(f => f.FieldKey).Should().OnlyHaveUniqueItems();
    }

    // ------------------------------------------------------------- AC2 — blindaje

    [Fact]
    public void VehiculoBlindadoMarcaBlindaje()
    {
        var m = Traspaso(("is_armored_vehicle", "true"));

        Campo(m, "blindaje").Should().Be("true");
        MarcaTransformacion(m).Should().BeTrue();
    }

    // ------------------------------------------------------------- AC3 — inscripción

    [Fact]
    public void InscripcionDePrendaEsRegistrarConElAcreedorDeV1()
    {
        var m = Matricula(("registered_pledge", "true"), ("pledge_in_favour", "BANCOLOMBIA SUFI"));

        var prenda = m.Prendas.Should().ContainSingle().Subject;
        prenda.Decision.Should().Be(PrendaDecision.Registrar);
        prenda.AccionFamilia.Should().Be(PrendaAccionFamilia.Constitucion);
        prenda.Estado.Should().Be(PrendaEstado.Vigente);
        prenda.AcreedorNombre.Should().Be("BANCOLOMBIA SUFI");
        prenda.ProcedureInstanceId.Should().Be(m.Instance.Id);
        MarcaPrenda(m).Should().BeTrue();
    }

    // ------------------------------------------------------------- AC4 — levantamiento

    [Fact]
    public void LevantamientoEsLevantarConElAcreedorDelRunt()
    {
        var m = Traspaso(
            ("has_garment_lifting", "true"),
            ("pledge_in_favour", "TEXTO LIBRE"),
            ("warranty_creditor_name", "BANCO SANTANDER COLOMBIA S.A."),
            ("warranty_creditor_document_number", "8909039388"));

        var prenda = m.Prendas.Should().ContainSingle().Subject;
        prenda.Decision.Should().Be(PrendaDecision.Levantar);
        prenda.AccionFamilia.Should().Be(PrendaAccionFamilia.Levantamiento);
        prenda.AcreedorNombre.Should().Be("BANCO SANTANDER COLOMBIA S.A.");
        prenda.AcreedorDocumento.Should().Be("8909039388");
        MarcaPrenda(m).Should().BeTrue();
    }

    [Fact]
    public void InscripcionYLevantamientoConvivenUnaPorFamilia()
    {
        var m = Traspaso(
            ("registered_pledge", "true"),
            ("pledge_in_favour", "MAQUIMAS"),
            ("has_garment_lifting", "true"),
            ("warranty_creditor_name", "SANTANDER"));

        m.Prendas.Select(p => p.AccionFamilia).Should().BeEquivalentTo(
            [PrendaAccionFamilia.Constitucion, PrendaAccionFamilia.Levantamiento]);
        m.Prendas.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    // ------------------------------------------------------------- AC5 — gravamen del RUNT sin gestionar

    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    public void AcreedorDelRuntSinAccionEsOmitirYNoMarca(string omitido)
    {
        var m = Traspaso(
            ("warranty_creditor_name", "BANCO SANTANDER COLOMBIA S.A."),
            ("warranty_creditor_omit_garment", omitido));

        var prenda = m.Prendas.Should().ContainSingle().Subject;
        prenda.Decision.Should().Be(PrendaDecision.Omitir);
        prenda.AccionFamilia.Should().BeNull();
        prenda.AcreedorNombre.Should().Be("BANCO SANTANDER COLOMBIA S.A.");
        MarcaPrenda(m).Should().BeFalse("un nativo que omite el gravamen tampoco lleva la marca");
    }

    // ------------------------------------------------------------- AC6 — nada que migrar

    [Fact]
    public void SinPrendaNiTransformacionNoEscribeNadaYConservaLosExtras()
    {
        var m = Traspaso(
            ("registered_pledge", "false"),
            ("has_garment_lifting", "false"),
            ("switch_vehicle_color", "false"),
            ("new_vehicle_color", "AZUL"));

        m.Prendas.Should().BeEmpty();
        Campo(m, "cambio_color").Should().BeNull();
        Campo(m, "vehicle_color_runt").Should().BeNull();
        MarcaTransformacion(m).Should().BeFalse();
        MarcaPrenda(m).Should().BeFalse();
        Campo(m, "legacy_v1_extras").Should().BeNull();
        m.FieldValues.Single(f => f.FieldKey == "legacy_v1_extras").ValueJson
            .Should().Contain("\"registered_pledge\":\"false\"").And.Contain("\"new_vehicle_color\":\"AZUL\"");
    }

    [Fact]
    public void LosIdsDePrendaSonDeterministicosParaQueReMigrarNoDuplique()
    {
        var a = Matricula(("registered_pledge", "true"), ("pledge_in_favour", "SUFI"));
        var b = Matricula(("registered_pledge", "true"), ("pledge_in_favour", "SUFI"));

        a.Prendas.Single().Id.Should().Be(b.Prendas.Single().Id);
    }
}
