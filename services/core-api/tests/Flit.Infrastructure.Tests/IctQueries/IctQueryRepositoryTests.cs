using Flit.Analytics.Application.IctQueries;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Queries.Domain;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.IctQueries;

/// <summary>
/// Consultas sobre pre-trámites de ICT.
///
/// <para>A diferencia de <c>CompanyQueryRepositoryTests</c> (LINQ sobre EF InMemory),
/// <see cref="IctQueryRepository"/> lee <c>ict.*</c> con SQL crudo sobre la conexión real de
/// <see cref="Flit.Infrastructure.Persistence.FlitDbContext"/> — el proveedor InMemory de EF no
/// expone una <c>DbConnection</c> real, así que la carga de filas (<c>LoadRowsAsync</c>) y el
/// catálogo con opciones (<c>GetFieldsAsync</c>) no son testeables sin una base Postgres real. Lo
/// que SÍ es puro y se prueba aquí exhaustivamente es la lógica que no toca la base: la resolución
/// del estado del pipeline (<see cref="IctQueryRepository.ResolveEstado"/>, expuesta
/// <c>internal</c> vía <c>InternalsVisibleTo</c>) y la combinación de comentarios de validación.
/// El catálogo de campos y las consultas de fábrica se cubren en
/// <c>IctQueryFieldCatalogTests</c>/<c>IctFactoryQueriesTests</c>.</para>
/// </summary>
public sealed class IctQueryRepositoryTests
{
    // ── ResolveEstado — regla de precedencia: borrador_creado gana SIEMPRE que haya
    // procedure_instance_id, sin importar qué diga process_status_id. ─────────────────────────

    [Theory]
    [InlineData((short)1)]
    [InlineData((short)2)]
    [InlineData((short)4)]
    [InlineData((short)6)]
    [InlineData((short)99)]
    public void ResolveEstado_ConBorrador_SiempreEsBorradorCreado(short processStatusId)
    {
        IctQueryRepository.ResolveEstado(processStatusId, tieneBorrador: true, businessDateValidation: null)
            .Should().Be("borrador_creado");
    }

    [Fact]
    public void ResolveEstado_SinBorrador_Estado1_EsRecibido()
    {
        IctQueryRepository.ResolveEstado(1, tieneBorrador: false, businessDateValidation: null)
            .Should().Be("recibido");
    }

    [Fact]
    public void ResolveEstado_SinBorrador_Estado2_SinValidacionDeNegocio_EsEnValidacionDeNegocio()
    {
        IctQueryRepository.ResolveEstado(2, tieneBorrador: false, businessDateValidation: null)
            .Should().Be("en_validacion_negocio");
    }

    [Fact]
    public void ResolveEstado_SinBorrador_Estado2_ConValidacionDeNegocio_EsEnValidacionExterna()
    {
        IctQueryRepository
            .ResolveEstado(2, tieneBorrador: false, businessDateValidation: DateTimeOffset.UtcNow)
            .Should().Be("en_validacion_externa");
    }

    [Fact]
    public void ResolveEstado_SinBorrador_Estado4_EsConNovedades()
    {
        IctQueryRepository.ResolveEstado(4, tieneBorrador: false, businessDateValidation: null)
            .Should().Be("con_novedades");
    }

    [Theory]
    [InlineData((short)6)]
    [InlineData((short)3)]
    [InlineData((short)99)]
    public void ResolveEstado_SinBorrador_CualquierOtroEstado_EsAnulado(short processStatusId)
    {
        IctQueryRepository.ResolveEstado(processStatusId, tieneBorrador: false, businessDateValidation: null)
            .Should().Be("anulado");
    }

    // ── CombineComentarios — texto libre, sin taxonomía de códigos detrás. ────────────────────

    [Fact]
    public void CombineComentarios_ConLosDos_LosUneConSeparador()
    {
        IctQueryRepository.CombineComentarios("SOAT vencido", "RUNT no encontró el vehículo")
            .Should().Be("SOAT vencido · RUNT no encontró el vehículo");
    }

    [Fact]
    public void CombineComentarios_SoloNegocio_DevuelveSoloEse()
    {
        IctQueryRepository.CombineComentarios("Documento comprador inválido", null)
            .Should().Be("Documento comprador inválido");
    }

    [Fact]
    public void CombineComentarios_SoloExterna_DevuelveSoloEse()
    {
        IctQueryRepository.CombineComentarios(null, "RTM no vigente")
            .Should().Be("RTM no vigente");
    }

    [Fact]
    public void CombineComentarios_AmbosVaciosOBlancos_DevuelveNull()
    {
        IctQueryRepository.CombineComentarios(null, "   ").Should().BeNull();
        IctQueryRepository.CombineComentarios(string.Empty, null).Should().BeNull();
    }

    // ── El motor genérico, amarrado al catálogo de ICT: la normalización cae al defecto correcto ─

    [Fact]
    public void Normalize_SinDefinicion_UsaElDefectoDelCatalogo()
    {
        var definition = IctQueryFieldCatalog.Normalize(null);

        definition.Fechas.Campo.Should().Be(IctQueryDateField.Registro);
        definition.SortBy.Should().Be(IctQuerySort.Registrado);
        definition.Condiciones.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_CondicionConCampoDesconocido_SeDescarta()
    {
        var definition = IctQueryFieldCatalog.Normalize(new QueryDefinition(
            new QueryDateFilter(IctQueryDateField.Registro, QueryRangePreset.Ultimos30),
            [new QueryCondition("campo_que_no_existe", QueryOperator.EsAlguno, ["x"])],
            []));

        definition.Condiciones.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_CondicionValida_SeConserva()
    {
        var definition = IctQueryFieldCatalog.Normalize(new QueryDefinition(
            new QueryDateFilter(IctQueryDateField.Registro, QueryRangePreset.Ultimos30),
            [new QueryCondition(IctQueryFieldCatalog.TieneNovedades, QueryOperator.EsAlguno, ["true"])],
            []));

        definition.Condiciones.Should().ContainSingle()
            .Which.FieldId.Should().Be(IctQueryFieldCatalog.TieneNovedades);
    }
}
