using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Application.BulkTramites.Processing;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Mapeo columna del Excel → vocabulario del wizard (HU #12523). Es el punto donde un error no
/// falla, sale mal: un trámite creado con el documento del vendedor en la casilla del comprador se
/// crea igual y nadie se entera hasta el organismo.
/// </summary>
public sealed class BulkTramitesRowMapperTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();

    private static Dictionary<string, string?> Fila(params (string Key, string Value)[] pares) =>
        pares.ToDictionary(p => p.Key, p => (string?)p.Value);

    [Fact]
    public void Matricula_ElPropietarioSePersisteComoComprador_PorqueElDominioNoTieneRolPropietario()
    {
        var fila = Fila(
            ("vin", "9BWZZZ377VT004251"),
            ("propietario_tipo_documento", "CC"),
            ("propietario_numero_documento", "123456789"),
            ("propietario_email", "juan@example.com"),
            ("propietario_celular", "3001112233"),
            ("propietario_ciudad", "Bogotá"),
            ("propietario_direccion", "Calle 1 # 2-3"));

        var ctx = BulkTramitesRowMapper.Map(BulkTramitesTemplateType.Matricula, Tenant, Usuario, fila);

        ctx.ProcedureTypeCode.Should().Be(BulkTramitesRowMapper.MatriculaProcedureTypeCode);
        ctx.Vin.Should().Be("9BWZZZ377VT004251");
        ctx.Actors.Should().ContainSingle();
        ctx.Actors[0].Rol.Should().Be("comprador");
        ctx.Actors[0].NumeroDocumento.Should().Be("123456789");
        ctx.Actors[0].Ordinal.Should().Be(1);
        ctx.Actors[0].Porcentaje.Should().BeNull();

        // El nombre no viene del Excel: lo pone el procesador con la consulta al RUNT. Los datos
        // de contacto sí, y viajan tal cual al guardado de actores.
        ctx.Actors[0].NombreCompleto.Should().BeEmpty();
        ctx.Actors[0].Telefono.Should().Be("3001112233");
        ctx.Actors[0].Ciudad.Should().Be("Bogotá");
        ctx.Actors[0].Direccion.Should().Be("Calle 1 # 2-3");
    }

    [Fact]
    public void Traspaso_ConsultaConElDocumentoDelVendedor_NoDelComprador()
    {
        // El RUNT conoce al propietario ACTUAL: consultar con el documento del comprador no
        // devolvería el vehículo.
        var fila = Fila(
            ("placa", "ABC123"),
            ("comprador_1_tipo_documento", "CC"), ("comprador_1_numero_documento", "111"),
            ("vendedor_1_tipo_documento", "CE"), ("vendedor_1_numero_documento", "999"));

        var ctx = BulkTramitesRowMapper.Map(BulkTramitesTemplateType.Traspaso, Tenant, Usuario, fila);

        ctx.OwnerDocumentType.Should().Be("CE");
        ctx.OwnerDocumentNumber.Should().Be("999");
        ctx.Plate.Should().Be("ABC123");
    }

    [Fact]
    public void Traspaso_MapeaLosCuatroSlotsPorLado_ConSuOrdinalYPorcentaje()
    {
        var fila = Fila(
            ("placa", "ABC123"),
            ("comprador_1_numero_documento", "1"), ("comprador_1_porcentaje", "60"),
            ("comprador_2_numero_documento", "2"), ("comprador_2_porcentaje", "40"),
            ("vendedor_1_numero_documento", "9"));

        var ctx = BulkTramitesRowMapper.Map(BulkTramitesTemplateType.Traspaso, Tenant, Usuario, fila);

        ctx.Actors.Should().HaveCount(3);

        var compradores = ctx.Actors.Where(a => a.Rol == "comprador").ToList();
        compradores.Should().HaveCount(2);
        compradores[0].Ordinal.Should().Be(1);
        compradores[0].Porcentaje.Should().Be(60m);
        compradores[1].Ordinal.Should().Be(2);
        compradores[1].Porcentaje.Should().Be(40m);

        ctx.Actors.Single(a => a.Rol == "vendedor").Porcentaje.Should().BeNull();
    }

    [Fact]
    public void Traspaso_SlotVacio_NoProduceActorFantasma()
    {
        var fila = Fila(
            ("placa", "ABC123"),
            ("comprador_1_numero_documento", "1"),
            ("comprador_2_email", "escrito-por-error-sin-documento@example.com"),
            ("vendedor_1_numero_documento", "9"));

        var ctx = BulkTramitesRowMapper.Map(BulkTramitesTemplateType.Traspaso, Tenant, Usuario, fila);

        ctx.Actors.Should().HaveCount(2);
    }

    [Fact]
    public void Otros_TomaElTipoDeTramiteDeLaColumna_YElRolQueEscribioElUsuario()
    {
        var fila = Fila(
            ("tipo_tramite", "CAMBIO_COLOR"),
            ("placa", "ABC123"),
            ("actor_1_rol", "Vendedor"),
            ("actor_1_tipo_documento", "CC"),
            ("actor_1_numero_documento", "555"));

        var ctx = BulkTramitesRowMapper.Map(BulkTramitesTemplateType.Otros, Tenant, Usuario, fila);

        ctx.ProcedureTypeCode.Should().Be("CAMBIO_COLOR");
        ctx.Actors.Should().ContainSingle();
        ctx.Actors[0].Rol.Should().Be("vendedor");
        ctx.OwnerDocumentNumber.Should().Be("555");
    }

    [Fact]
    public void Otros_SinRolEscrito_CaeEnComprador_QueEsElRolDelTitular()
    {
        var fila = Fila(
            ("tipo_tramite", "DUPLICADO_PLACA"),
            ("placa", "ABC123"),
            ("actor_1_numero_documento", "555"));

        var ctx = BulkTramitesRowMapper.Map(BulkTramitesTemplateType.Otros, Tenant, Usuario, fila);

        ctx.Actors[0].Rol.Should().Be("comprador");
    }
}
