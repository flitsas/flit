using Flit.Ict.Domain.Entities;
using Flit.Ict.Infrastructure.Jobs;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

/// <summary>
/// Bug #13304 (capa 3): las advertencias actors_warning / commercial_warning que core-api devuelve al
/// materializar quedan visibles en el master (comentarios que muestran el GET de estado y la bandeja),
/// en vez de solo loguearse. Lo usa <see cref="SendToCoreApiJob"/>.
/// <para>Uso de ejemplo:</para>
/// <code>
/// var novedad = MaterializacionNovedades.Mensaje(result.ErrorCode);
/// if (novedad is not null) MaterializacionNovedades.Aplicar(master, novedad);
/// </code>
/// </summary>
public sealed class MaterializacionNovedadesTests
{
    [Fact]
    public void Warning_de_actores_y_comercial_marca_novedad_visible_en_el_master()
    {
        var master = new ExternalIntegrationMaster
        {
            ProcessStatusId = 5,
            ExternalCommentsValidation = "advertencia previa;",
        };

        var novedad = MaterializacionNovedades.Mensaje(
            "seed_warning:x;actors_warning:invalid_document_type;commercial_warning:invalid_valor_venta");
        novedad.Should().NotBeNull();
        MaterializacionNovedades.Aplicar(master, novedad!);

        master.BusinessCommentsValidation.Should()
            .Contain("actors_warning:invalid_document_type")
            .And.Contain("commercial_warning:invalid_valor_venta")
            .And.NotContain("seed_warning");
        master.ExternalCommentsValidation.Should().StartWith("advertencia previa;")
            .And.Contain("actors_warning:invalid_document_type");
        master.ProcessStatusId.Should().Be(5, "el borrador existe: la novedad no cambia el estado");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("seed_warning:transit_office:not_found")]
    [InlineData("identity_warning:vendedor:datos_incompletos;attachments_warning:exception")]
    public void Sin_warning_de_actores_ni_comercial_no_hay_novedad(string? errorCode) =>
        MaterializacionNovedades.Mensaje(errorCode).Should().BeNull();

    [Fact]
    public void Visibles_quita_repetidos_y_espacios() =>
        MaterializacionNovedades.Visibles(" actors_warning:a ; actors_warning:a;commercial_warning:b ")
            .Should().Equal("actors_warning:a", "commercial_warning:b");

    [Fact]
    public void Mensaje_queda_acotado_para_el_webhook()
    {
        var largo = string.Join(';', Enumerable.Range(0, 60).Select(i => $"actors_warning:codigo_{i}"));
        MaterializacionNovedades.Mensaje(largo)!.Length.Should().BeLessThanOrEqualTo(MaterializacionNovedades.MaxLongitud);
    }

    [Theory]
    [InlineData("ot_runt_no_habilitado", "según el RUNT no está activo o no está habilitado")]
    [InlineData("ot_not_permitted", "no está habilitado para la compañía")]
    [InlineData("OT_NOT_AUTHORIZED_FOR_TYPE", "no está habilitado u operativo")]
    [InlineData("tenant_inactive", "no está activo")]
    [InlineData("network_inactive", "red")]
    public void MensajeError_traduce_los_codigos_de_organismo_y_conserva_el_codigo(string codigo, string fragmento)
    {
        var mensaje = MaterializacionNovedades.MensajeError(codigo);

        mensaje.Should().Contain(fragmento).And.EndWith($"({codigo})");
    }
    [Theory]
    [InlineData("placa_fuera_de_rango", "no pertenece al rango asignado")]
    [InlineData("placa_rango_sin_ot", "no se conoce el organismo de tránsito")]
    public void MensajeError_traduce_los_codigos_de_placa_de_rango_y_conserva_el_codigo(string codigo, string fragmento)
    {
        var mensaje = MaterializacionNovedades.MensajeError(codigo);

        mensaje.Should().Contain(fragmento).And.EndWith($"({codigo})");
    }

    [Fact]
    public void Warning_de_carroceria_fuera_de_catalogo_es_novedad_visible_con_mensaje_claro()
    {
        var master = new ExternalIntegrationMaster { ProcessStatusId = 5 };

        var novedad = MaterializacionNovedades.Mensaje("seed_warning:x;carroceria_warning:fuera_de_catalogo");
        novedad.Should().NotBeNull();
        MaterializacionNovedades.Aplicar(master, novedad!);

        novedad.Should().Contain("La carrocería enviada no corresponde al catálogo para la clase del vehículo consultado")
            .And.Contain("el gestor debe elegirla")
            .And.Contain("(carroceria_warning:fuera_de_catalogo)")
            .And.NotContain("seed_warning");
        master.BusinessCommentsValidation.Should().Contain("el gestor debe elegirla");
        master.ExternalCommentsValidation.Should().Contain("el gestor debe elegirla");
        master.ProcessStatusId.Should().Be(5, "el borrador existe: la novedad no cambia el estado");
    }

    [Fact]
    public void Warning_de_vehiculo_sin_carroceria_es_novedad_visible_con_mensaje_claro()
    {
        var novedad = MaterializacionNovedades.Mensaje("carroceria_warning:vehiculo_sin_carroceria");

        novedad.Should().NotBeNull();
        novedad.Should().Contain(
                "El vehículo no tiene carrocería registrada en el RUNT; el cambio de carrocería no aplica y no se incluyó en el trámite")
            .And.Contain("(carroceria_warning:vehiculo_sin_carroceria)");
    }

    [Fact]
    public void Advertencias_sin_texto_propio_se_publican_como_codigo()
    {
        MaterializacionNovedades.Mensaje("actors_warning:invalid_document_type")
            .Should().Be(MaterializacionNovedades.Prefijo + "actors_warning:invalid_document_type");
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("grpc_unavailable")]
    [InlineData("create_failed")]
    public void MensajeError_devuelve_null_para_codigos_no_mapeados(string? codigo) =>
        MaterializacionNovedades.MensajeError(codigo).Should().BeNull();
}
