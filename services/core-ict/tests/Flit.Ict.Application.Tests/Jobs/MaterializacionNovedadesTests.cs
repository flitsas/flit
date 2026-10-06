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
}
