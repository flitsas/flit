using Flit.Ict.Application.Register;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Register;

/// <summary>
/// Bug #13109 punto 7: el register rechaza la fila cuando ya hay, en el mismo tenant, un pre-trámite en
/// estado interno 1 o 2 con la misma clave que usa el duplicado dentro del lote. El predicado es el que
/// EF traduce a SQL; aquí se evalúa en memoria contra filas guardadas simuladas.
/// </summary>
/// <example>
/// var predicado = PreTramiteClave.EnProcesoConLaMismaClave(nuevo).Compile();
/// guardados.Any(predicado); // true si hay otro en proceso con la misma clave
/// </example>
public sealed class PreTramiteClaveTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ExternalIntegrationMaster Guardado(
        int tipo, short estado, string plate = "IWL38D", string? vin = null, Guid? tenant = null,
        DateTime? deletedAt = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenant ?? Tenant,
            TransactionType = tipo,
            ProcessStatusId = estado,
            Plate = plate,
            Vin = vin,
            DeletedAt = deletedAt,
        };

    private static ExternalIntegrationMaster Nuevo(int tipo, string plate = "IWL38D", string? vin = null) =>
        Guardado(tipo, 1, plate, vin);

    private static bool Choca(ExternalIntegrationMaster nuevo, params ExternalIntegrationMaster[] guardados) =>
        guardados.Any(PreTramiteClave.EnProcesoConLaMismaClave(nuevo).Compile());

    [Theory]
    [InlineData((short)1, true)]
    [InlineData((short)2, true)]
    [InlineData((short)4, false)]
    [InlineData((short)5, false)]
    [InlineData((short)6, false)]
    public void Traspaso_choca_solo_con_la_misma_placa_en_estado_1_o_2(short estado, bool choca)
    {
        Choca(Nuevo(3), Guardado(3, estado)).Should().Be(choca);
    }

    [Fact]
    public void Bilateral_y_unilateral_comparten_clave_de_placa()
    {
        Choca(Nuevo(3), Guardado(4, 2)).Should().BeTrue();
        Choca(Nuevo(4), Guardado(3, 1)).Should().BeTrue();
    }

    [Fact]
    public void Matricula_choca_por_vin_y_no_por_placa()
    {
        Choca(Nuevo(1, plate: "", vin: "12Y3SR456RJ789123"), Guardado(2, 1, plate: "", vin: "12Y3SR456RJ789123"))
            .Should().BeTrue();
        Choca(Nuevo(1, vin: "12Y3SR456RJ789123"), Guardado(1, 1, vin: "99Y3SR456RJ789999"))
            .Should().BeFalse();
    }

    [Fact]
    public void Otro_tenant_o_borrado_no_bloquea()
    {
        Choca(Nuevo(3), Guardado(3, 1, tenant: Guid.NewGuid())).Should().BeFalse();
        Choca(Nuevo(3), Guardado(3, 1, deletedAt: DateTime.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public void Otros_tramites_chocan_por_placa_y_tipo()
    {
        Choca(Nuevo(5), Guardado(5, 2)).Should().BeTrue();
        Choca(Nuevo(5), Guardado(7, 2)).Should().BeFalse();
        Choca(Nuevo(5), Guardado(3, 2)).Should().BeFalse();
    }

    [Fact]
    public void La_propia_fila_no_choca_consigo_misma()
    {
        var nuevo = Nuevo(3);
        Choca(nuevo, nuevo).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void La_clave_es_la_misma_que_la_del_duplicado_dentro_del_lote(int tipo)
    {
        var row = new RegisterRowInput(TransactionType: tipo, TransactionOperation: 1, Plate: " iwl38d ", Vin: "12y3sr456rj789123");

        IctPayloadNormalizer.DedupKey(row).Should().Be(PreTramiteClave.Texto(tipo, row.Plate, row.Vin));
    }
}
