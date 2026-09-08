using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using FluentAssertions;
using Xunit;

namespace Flit.DataMigration.Tests.Mapping;

/// <summary>
/// HU #12152 (Feature #12150) — el migrador deja de inventarse el radicado.
/// <para>
/// Antes ponía <c>MIG-TR-{id}</c> / <c>MIG-MI-{id}</c> para que un trámite traído de V1 se
/// reconociera a simple vista. Con el consecutivo global eso ya no cabe: un valor con prefijo
/// viola <c>ck_procedure_instances_reference_numerico</c> y la creación falla. El radicado lo
/// asigna ahora el <c>DEFAULT</c> de la columna.
/// </para>
/// <para>
/// La trazabilidad a V1 no se pierde, y por eso estas pruebas comprueban las dos caras: que el
/// mapper NO fija el radicado, y que sigue marcando <c>IsMigrated</c>. El id de V1 vive en
/// <c>migration.migration_map</c> (la libreta de idempotencia), así que el prefijo era una
/// segunda copia de un dato que ya estaba guardado en otro sitio.
/// </para>
/// </summary>
public sealed class RadicadoConsecutivoTests
{
    private static MappingContext Contexto() => new()
    {
        TenantId = Guid.Parse("0ad1c0de-0000-4000-8000-000000000001"),
        ProcedureTypeId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        SystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        OwnerEntityId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        BuyerEntityId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
    };

    private static V1SourceRecord Registro(string tabla, long id) => new()
    {
        Id = id,
        SourceTable = tabla,
        ProcessStatus = 1,
        Columns = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
        StatusHistory = [],
    };

    [Fact]
    public void ElTraspasoMigradoNoTraeRadicadoPropio()
    {
        var mapeado = TransferMapper.Map(Registro("vehicle_transfer_master", 8617), Contexto());

        // Vacío = «que lo ponga la base». Cualquier texto con letras reventaría el CHECK.
        mapeado.Instance.ReferenceNumber.Should().BeEmpty();
        mapeado.Instance.ReferenceNumber.Should().NotContain("MIG");
    }

    [Fact]
    public void LaMatriculaMigradaNoTraeRadicadoPropio()
    {
        var mapeado = RegistrationMapper.Map(Registro("vehicle_registration_master", 26350), Contexto());

        mapeado.Instance.ReferenceNumber.Should().BeEmpty();
        mapeado.Instance.ReferenceNumber.Should().NotContain("MIG");
    }

    [Theory]
    [InlineData("vehicle_transfer_master", 8617)]
    [InlineData("vehicle_registration_master", 26350)]
    public void LaProcedenciaDeV1SigueQuedandoRegistrada(string tabla, long id)
    {
        var registro = Registro(tabla, id);
        var mapeado = tabla == "vehicle_transfer_master"
            ? TransferMapper.Map(registro, Contexto())
            : RegistrationMapper.Map(registro, Contexto());

        // Lo que sustituye al prefijo MIG-: la marca de migrado y el id de V1 que viaja al
        // migration_map. Si alguien quitara esto, un trámite migrado sería indistinguible.
        mapeado.Instance.IsMigrated.Should().BeTrue();
        mapeado.V1Id.Should().Be(id);
        mapeado.V1Table.Should().Be(tabla);
    }
}
