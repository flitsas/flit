using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.ExternalClients;
using Flit.Ict.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// HU #14602 (Feature #14601): advertencias visibles de color (core-api) y de combustible sin equivalencia
/// (core-ict). Uso: <c>MaterializacionNovedades.Mensaje(MaterializacionNovedades.Combinar(errorCode,
/// IctDraftFieldValuesMapper.AdvertenciasLocales(master)))</c>.
/// </summary>
public sealed class AdvertenciasColorCombustibleHu14602Tests
{
    private const string Combustible = "combustible_warning:sin_equivalencia";

    private static ExternalIntegrationMaster Master(int tramite, short? fuel, params int[] transformaciones)
    {
        var master = new ExternalIntegrationMaster
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TransactionType = tramite,
            NewVehicleFuelType = fuel,
        };
        foreach (var codigo in transformaciones)
        {
            master.Transformations.Add(new ExternalIntegrationMasterTransformation
            {
                MasterId = master.Id,
                TenantId = master.TenantId,
                IdTransformationType = codigo,
            });
        }

        return master;
    }

    [Fact]
    public void Color_fuera_de_catalogo_es_visible_con_texto_y_codigo()
    {
        MaterializacionNovedades.Mensaje("color_warning:fuera_de_catalogo").Should()
            .Contain("El color enviado no corresponde al catálogo de colores; el gestor debe elegirlo")
            .And.EndWith("(color_warning:fuera_de_catalogo)");
    }

    [Fact]
    public void Color_se_combina_con_otras_advertencias_visibles()
    {
        var mensaje = MaterializacionNovedades.Mensaje(
            "seed_warning:x;color_warning:fuera_de_catalogo;carroceria_warning:fuera_de_catalogo");

        mensaje.Should().Contain("(color_warning:fuera_de_catalogo)")
            .And.Contain("(carroceria_warning:fuera_de_catalogo)")
            .And.NotContain("seed_warning");
    }

    [Theory]
    [InlineData(1, (short)4, 9)]
    [InlineData(1, (short)9, 9)]
    [InlineData(9, (short)4, 0)]
    [InlineData(9, (short)12, 0)]
    [InlineData(3, (short)50, 9)]
    [InlineData(9, (short)50, 0)]
    public void Combustible_declarado_sin_equivalencia_genera_advertencia(int tramite, short fuel, int transformacion)
    {
        var master = transformacion == 0 ? Master(tramite, fuel) : Master(tramite, fuel, transformacion);

        IctDraftFieldValuesMapper.AdvertenciasLocales(master).Should().Equal(Combustible);
        // El borrador se crea igual: bandera sí, vehicle_fuel no.
        var claves = IctDraftFieldValuesMapper.Map(master).Select(f => f.FieldKey).ToList();
        if (transformacion == 9)
        {
            claves.Should().Contain("cambio_combustible");
        }

        claves.Should().NotContain("vehicle_fuel");
    }

    [Fact]
    public void Combustible_con_equivalencia_no_genera_advertencia_y_envia_vehicle_fuel()
    {
        var master = Master(3, 1, 9);

        IctDraftFieldValuesMapper.AdvertenciasLocales(master).Should().BeEmpty();
        IctDraftFieldValuesMapper.Map(master).Should().Contain(f => f.FieldKey == "vehicle_fuel" && f.ValueText == "GASOLINA");
    }

    [Fact]
    public void Sin_combustible_solo_la_bandera_sin_advertencia()
    {
        var master = Master(3, null, 9);

        IctDraftFieldValuesMapper.AdvertenciasLocales(master).Should().BeEmpty();
        var claves = IctDraftFieldValuesMapper.Map(master).Select(f => f.FieldKey).ToList();
        claves.Should().Contain("cambio_combustible").And.NotContain("vehicle_fuel");
    }

    [Fact]
    public void Combustible_sin_equivalencia_pero_sin_declarar_el_cambio_no_advierte() =>
        IctDraftFieldValuesMapper.AdvertenciasLocales(Master(3, 4, 5)).Should().BeEmpty();

    [Fact]
    public void Texto_del_combustible_es_claro_y_conserva_el_codigo() =>
        MaterializacionNovedades.Mensaje(Combustible).Should()
            .Contain("El combustible enviado no tiene equivalencia en el catálogo; el gestor debe elegirlo")
            .And.EndWith($"({Combustible})");

    [Fact]
    public void Combinar_agrega_la_local_sin_duplicar()
    {
        MaterializacionNovedades.Combinar("seed_warning:x", [Combustible]).Should().Be("seed_warning:x;" + Combustible);
        MaterializacionNovedades.Combinar(null, [Combustible]).Should().Be(Combustible);
        MaterializacionNovedades.Combinar("a;" + Combustible, [Combustible]).Should().Be("a;" + Combustible);
        MaterializacionNovedades.Combinar("a", []).Should().Be("a");
        MaterializacionNovedades.Combinar(null, []).Should().BeNull();
    }

    [Fact]
    public void Combinado_respeta_el_tope_de_longitud()
    {
        var largo = string.Join(';', Enumerable.Range(0, 60).Select(i => $"actors_warning:codigo_{i}"));
        MaterializacionNovedades.Mensaje(MaterializacionNovedades.Combinar(largo, [Combustible]))!
            .Length.Should().BeLessThanOrEqualTo(MaterializacionNovedades.MaxLongitud);
    }

    [Fact]
    public async Task Job_borrador_creado_publica_la_advertencia_local_junto_a_la_del_reply_sin_cambiar_estado()
    {
        var master = Master(3, 4, 9);
        var instanceId = Guid.NewGuid();
        string? novedadRegistrada = null;

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, new CreateDraftResult(instanceId, "REF", "borrador", "color_warning:fuera_de_catalogo"),
            novedadRunt: null, reencolado: false,
            (_, n, _) => { novedadRegistrada = n; return Task.CompletedTask; },
            (_, _) => Task.CompletedTask,
            Substitute.For<IIctVehicleSnapshotReader>(), NullLogger.Instance, TestContext.Current.CancellationToken);

        master.ProcessStatusId.Should().Be(5);
        novedadRegistrada.Should().Contain("(color_warning:fuera_de_catalogo)").And.Contain($"({Combustible})");
        master.BusinessCommentsValidation.Should().Contain("el gestor debe elegirlo");
        master.ExternalCommentsValidation.Should().Contain($"({Combustible})");
    }

    [Fact]
    public async Task Job_sin_advertencias_no_deja_novedad()
    {
        var master = Master(3, 1, 9);
        string? novedadRegistrada = "x";

        await SendToCoreApiJob.ResolverEnvioAsync(
            master, new CreateDraftResult(Guid.NewGuid(), "REF", "borrador", null),
            novedadRunt: null, reencolado: false,
            (_, n, _) => { novedadRegistrada = n; return Task.CompletedTask; },
            (_, _) => Task.CompletedTask,
            Substitute.For<IIctVehicleSnapshotReader>(), NullLogger.Instance, TestContext.Current.CancellationToken);

        novedadRegistrada.Should().BeNull();
        master.ProcessStatusId.Should().Be(5);
    }
}
