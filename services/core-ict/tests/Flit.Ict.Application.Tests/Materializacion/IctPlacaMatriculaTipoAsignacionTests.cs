using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using Flit.Ict.Infrastructure.Persistence.Sql;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Materializacion;

/// <summary>
/// Hallazgo QA de ICT — la placa de la matrícula viaja a core-api según <c>plate_assignment_type</c>:
/// 2 = dígito de preferencia (NUNCA <c>plate</c>), 3 = placa completa, 4 = placa de un rango del OT.
/// Antes solo viajaba <c>plate</c> y un dígito "5" llegaba como placa.
/// <para>Uso de ejemplo:
/// <code>
/// var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(master, tipo, resolver, log: null, ct);
/// request.FieldValues // tipo 2 con "5" → plate_preferred_last_digit="5", sin plate
/// </code></para>
/// </summary>
public sealed class IctPlacaMatriculaTipoAsignacionTests
{
    private static readonly DraftProcedureType TipoMatricula = new(
        "MATRICULA_NUEVA", "MATRICULA", RequiresCommercialValue: false, ResolvesTransitOfficeFromRunt: false);

    private static ExternalIntegrationMaster Master(
        int transactionType, short? tipoPlaca, string placa, string vin = "9BWZZZ377VT004251") => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TransactionType = transactionType,
            PlateAssignmentType = tipoPlaca,
            Plate = placa,
            Vin = vin,
        };

    private static async Task<Dictionary<string, string>> FieldValuesAsync(
        ExternalIntegrationMaster master, DraftProcedureType? tipo = null)
    {
        var resolver = Substitute.For<IAttachmentDocTypeResolver>();
        var request = await IctGrpcProcedureDraftClient.BuildRequestAsync(
            master, tipo ?? TipoMatricula, resolver, log: null, TestContext.Current.CancellationToken);
        request.FieldValues.Select(f => f.FieldKey).Should().OnlyHaveUniqueItems();
        return request.FieldValues.ToDictionary(f => f.FieldKey, f => f.ValueText);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Tipo2ConDigito_EnviaElDigitoDePreferencia_YNuncaPlate(int tramite)
    {
        var fv = await FieldValuesAsync(Master(tramite, 2, "5"));

        fv.Should().Contain("plate_assignment_type", "2").And.Contain("plate_preferred_last_digit", "5");
        fv.Keys.Should().NotContain("plate");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("55")]
    [InlineData("A")]
    public async Task Tipo2SinDigitoValido_NoEnviaPlateNiDigito(string valor)
    {
        var fv = await FieldValuesAsync(Master(1, 2, valor));

        fv.Should().Contain("plate_assignment_type", "2");
        fv.Keys.Should().NotContain(["plate", "plate_preferred_last_digit"]);
    }

    [Theory]
    [InlineData((short)3)]
    [InlineData((short)4)]
    public async Task Tipo3Y4_EnvianLaPlacaNormalizada_ConElTipoParaTrazabilidad(short tipo)
    {
        var fv = await FieldValuesAsync(Master(1, tipo, " abc123 "));

        fv.Should().Contain("plate", "ABC123").And.Contain("plate_assignment_type", tipo.ToString());
        fv.Keys.Should().NotContain("plate_preferred_last_digit");
    }

    [Fact]
    public async Task MatriculaSinTipoDeAsignacion_ConservaElComportamientoPrevio()
    {
        var fv = await FieldValuesAsync(Master(1, null, "ABC123"));

        fv.Should().Contain("plate", "ABC123");
        fv.Keys.Should().NotContain("plate_assignment_type");
    }

    [Fact]
    public async Task Traspaso_IgnoraElTipoDeAsignacion_YEnviaLaPlacaTalCual()
    {
        var traspaso = new DraftProcedureType(
            "TRASPASO", "TRASPASO", RequiresCommercialValue: true, ResolvesTransitOfficeFromRunt: true);

        var fv = await FieldValuesAsync(Master(3, 2, "ABC123"), traspaso);

        fv.Should().Contain("plate", "ABC123");
        fv.Keys.Should().NotContain(["plate_assignment_type", "plate_preferred_last_digit"]);
    }

    [Theory]
    [InlineData(1, (short)2, true)]
    [InlineData(2, (short)3, true)]
    [InlineData(1, (short)4, true)]
    [InlineData(1, (short)1, false)]
    [InlineData(1, null, false)]
    [InlineData(3, (short)3, false)]
    public void PlacaPorTipoDeAsignacion_SoloEnMatriculaConTipo2a4(int tramite, short? tipo, bool esperado) =>
        IctDraftFieldValuesMapper.PlacaPorTipoDeAsignacion(Master(tramite, tipo, "ABC123")).Should().Be(esperado);
}
