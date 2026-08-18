using Flit.Analytics.Application.IctQueries;
using Flit.Queries.Domain;
using FluentAssertions;
using Xunit;

namespace Flit.Analytics.Application.Tests.IctQueries;

/// <summary>
/// El catálogo de campos consultables sobre pre-trámites de ICT: el contrato entre la UI y el
/// repositorio. Lo que importa probar aquí no es la lista en sí (eso lo verifica cualquiera abriendo
/// la consola), sino las reglas que, si se rompen, el usuario nunca ve: qué campos rinden cuentas en
/// el aviso de cobertura, y que "Compañía" exista solo como campo de catálogo — su exclusión para no
/// SuperAdmin es responsabilidad del repositorio, no de este catálogo.
/// </summary>
public sealed class IctQueryFieldCatalogTests
{
    [Theory]
    [InlineData(IctQueryFieldCatalog.Placa)]
    [InlineData(IctQueryFieldCatalog.Vin)]
    [InlineData(IctQueryFieldCatalog.Radicado)]
    public void IsIdentifier_PlacaVinYRadicado_SonIdentificadores(string fieldId)
    {
        IctQueryFieldCatalog.IsIdentifier(fieldId).Should().BeTrue();
    }

    [Theory]
    [InlineData(IctQueryFieldCatalog.Estado)]
    [InlineData(IctQueryFieldCatalog.TipoTramite)]
    [InlineData(IctQueryFieldCatalog.NumeroTransaccion)]
    public void IsIdentifier_OtrosCampos_NoSonIdentificadores(string fieldId)
    {
        IctQueryFieldCatalog.IsIdentifier(fieldId).Should().BeFalse();
    }

    [Fact]
    public void Fields_IncluyeTodosLosCamposAprobadosEnLaHU()
    {
        var ids = IctQueryFieldCatalog.Fields.Select(f => f.Id).ToList();

        ids.Should().Contain([
            IctQueryFieldCatalog.Placa,
            IctQueryFieldCatalog.Vin,
            IctQueryFieldCatalog.Radicado,
            IctQueryFieldCatalog.NumeroTransaccion,
            IctQueryFieldCatalog.Comentarios,
            IctQueryFieldCatalog.TipoTramite,
            IctQueryFieldCatalog.Estado,
            IctQueryFieldCatalog.Secretaria,
            IctQueryFieldCatalog.ClienteIntegracion,
            IctQueryFieldCatalog.TieneNovedades,
            IctQueryFieldCatalog.Prioritario,
            IctQueryFieldCatalog.TieneBorrador,
            IctQueryFieldCatalog.Compania,
        ]);
    }

    [Fact]
    public void Comentarios_SoloAdmiteBusquedaLibre_NoEsAlguno()
    {
        // No hay taxonomía de códigos de rechazo detrás de este campo (texto libre concatenado por
        // el SP externo de core-ict): ofrecer "es alguno de estos valores" sería prometer una lista
        // cerrada que no existe.
        var campo = IctQueryFieldCatalog.Find(IctQueryFieldCatalog.Comentarios);

        campo.Should().NotBeNull();
        campo!.Operators.Should().Contain(QueryOperator.Contiene);
    }

    [Fact]
    public void Compania_EsDeTipoOpcionYPerteneceAlGrupoAlcance()
    {
        var campo = IctQueryFieldCatalog.Find(IctQueryFieldCatalog.Compania);

        campo.Should().NotBeNull();
        campo!.Kind.Should().Be(QueryFieldKind.Opcion);
        campo.Group.Should().Be(IctQueryFieldCatalog.GrupoAlcance);
    }

    [Fact]
    public void DefaultDateField_EsRegistro()
    {
        ((IQueryFieldCatalog)IctQueryFieldCatalog.Instance).DefaultDateField
            .Should().Be(IctQueryDateField.Registro);
    }

    [Fact]
    public void DefaultSort_EsRegistrado()
    {
        ((IQueryFieldCatalog)IctQueryFieldCatalog.Instance).DefaultSort
            .Should().Be(IctQuerySort.Registrado);
    }

    [Fact]
    public void Find_CampoDesconocido_DevuelveNull()
    {
        IctQueryFieldCatalog.Find("no_existe").Should().BeNull();
    }
}
