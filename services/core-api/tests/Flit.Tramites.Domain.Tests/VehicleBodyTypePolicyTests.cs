using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Precondición registral del cambio de carrocería: no se puede cambiar la carrocería de un vehículo
/// que el RUNT no reporta con ninguna.
/// </summary>
public sealed class VehicleBodyTypePolicyTests
{
    [Fact]
    public void SinCarroceriaEnRunt_Bloquea()
    {
        var block = VehicleBodyTypePolicy.Evaluar("CAMBIO_CARROCERIA", consultaRespondio: true, carroceriaReportada: null);

        block.Should().NotBeNull();
        block!.ProcedureType.Should().Be(VehicleBodyTypePolicy.ProcedureTypeCambioCarroceria);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CarroceriaVaciaOEnBlanco_CuentaComoAusente(string reportada)
    {
        // El RUNT puede devolver la clave con cadena vacía: sigue sin haber carrocería que sustituir.
        VehicleBodyTypePolicy
            .Evaluar("CAMBIO_CARROCERIA", consultaRespondio: true, carroceriaReportada: reportada)
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("SIN CARROCERIA")]
    [InlineData("sin carroceria")]
    [InlineData("  Sin   Carroceria  ")]
    [InlineData("SIN CARROCERÍA")]
    [InlineData("-")]
    public void SinCarroceriaComoVALOR_TambienBloquea(string reportada)
    {
        // El caso que se escapaba: una motocicleta NO trae el campo vacío, trae «SIN CARROCERIA», que
        // es una entrada real del catálogo oficial (código 0) y la única opción de su clase. Mirando
        // solo el vacío, el vehículo pasaba el pre-vuelo y llegaba a un paso donde el selector de
        // carrocería nueva no tenía ni una opción que ofrecer.
        VehicleBodyTypePolicy
            .Evaluar("CAMBIO_CARROCERIA", consultaRespondio: true, carroceriaReportada: reportada)
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("PICKUP")]
    [InlineData("ESTACAS")]
    [InlineData("FURGON")]
    [InlineData("CARROCERIA METALICA")]
    public void UnaCarroceriaDeVerdad_NoBloquea(string reportada)
    {
        // La contraparte: reconocer el centinela no puede tragarse valores que sí son carrocerías,
        // incluidos los que contienen la palabra.
        VehicleBodyTypePolicy.SinCarroceria(reportada).Should().BeFalse();
        VehicleBodyTypePolicy
            .Evaluar("CAMBIO_CARROCERIA", consultaRespondio: true, carroceriaReportada: reportada)
            .Should().BeNull();
    }

    [Fact]
    public void ConCarroceria_NoBloquea()
    {
        VehicleBodyTypePolicy
            .Evaluar("CAMBIO_CARROCERIA", consultaRespondio: true, carroceriaReportada: "PICKUP")
            .Should().BeNull();
    }

    [Fact]
    public void ProveedorSinResponder_NoBloquea()
    {
        // Diferencia deliberada con CF-03: aquí «no se sabe» NO es «no tiene». Convertir una caída del
        // RUNT en un trámite imposible de radicar castigaría al gestor por un fallo ajeno.
        VehicleBodyTypePolicy
            .Evaluar("CAMBIO_CARROCERIA", consultaRespondio: false, carroceriaReportada: null)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("TRASPASO_TRANSFERENCIA_DE_DOMINIO")]
    [InlineData("DUPLICADO_TARJETA")]
    [InlineData("BLINDAJE")]
    [InlineData("MATRICULA_NUEVA")]
    [InlineData(null)]
    public void OtrosTipos_NoExigenCarroceriaPrevia(string? code)
    {
        // Para el resto de trámites la carrocería es un dato descriptivo más: un vehículo que la traiga
        // vacía en el RUNT puede traspasarse o duplicar su tarjeta sin problema.
        VehicleBodyTypePolicy.ExigeCarroceriaPrevia(code).Should().BeFalse();
        VehicleBodyTypePolicy
            .Evaluar(code, consultaRespondio: true, carroceriaReportada: null)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("CAMBIO_CARROCERIA")]
    [InlineData("  cambio_carroceria  ")]
    public void ExigeCarroceriaPrevia_NormalizaElCodigo(string code)
    {
        VehicleBodyTypePolicy.ExigeCarroceriaPrevia(code).Should().BeTrue();
    }
}
