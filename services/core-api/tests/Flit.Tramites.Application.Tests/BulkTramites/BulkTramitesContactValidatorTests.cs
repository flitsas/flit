using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Application.BulkTramites.Parsing;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.BulkTramites;

/// <summary>
/// Réplica —sobre la fila del Excel, antes de que exista el trámite— de la obligatoriedad de los
/// datos de contacto que el wizard exige a cada actor. Salió de las pruebas en DEV de la Feature
/// #12519 (HU #12522): sin esto una fila de empresa sin ciudad ni dirección salía «Creado» pero el
/// trámite volvía a Actores con «Vendedor pendiente».
/// </summary>
public sealed class BulkTramitesContactValidatorTests
{
    private static Dictionary<string, string?> Fila(params (string Key, string Value)[] pares) =>
        pares.ToDictionary(p => p.Key, p => (string?)p.Value);

    private static IEnumerable<(string, string)> Contacto(string prefijo) =>
    [
        ($"{prefijo}_email", "a@b.co"),
        ($"{prefijo}_celular", "3001234567"),
        ($"{prefijo}_ciudad", "Bogotá"),
        ($"{prefijo}_direccion", "Calle 1 # 2-3"),
    ];

    [Fact]
    public void ActoresConLosCuatroDatos_NoError()
    {
        var fila = Fila(
        [
            ("comprador_1_numero_documento", "1"), .. Contacto("comprador_1"),
            ("vendedor_1_numero_documento", "9"), .. Contacto("vendedor_1"),
        ]);

        BulkTramitesContactValidator.Validate(BulkTramitesTemplateType.Traspaso, fila).Should().BeNull();
    }

    [Fact]
    public void EmpresaSinCiudadNiDireccion_MarcaLaFilaYDiceQueActor()
    {
        var fila = Fila(
        [
            ("comprador_1_numero_documento", "1"), .. Contacto("comprador_1"),
            ("vendedor_1_tipo_documento", "NIT"),
            ("vendedor_1_numero_documento", "890903938"),
            ("vendedor_1_email", "a@b.co"),
            ("vendedor_1_celular", "3001234567"),
        ]);

        BulkTramitesContactValidator.Validate(BulkTramitesTemplateType.Traspaso, fila)
            .Should().Be("datos_contacto_incompletos:vendedor_1");
    }

    [Fact]
    public void CampoSoloConEspacios_CuentaComoVacio()
    {
        var fila = Fila(
        [
            ("propietario_1_numero_documento", "1"),
            ("propietario_1_email", "a@b.co"),
            ("propietario_1_celular", "3001234567"),
            ("propietario_1_ciudad", "   "),
            ("propietario_1_direccion", "Calle 1 # 2-3"),
        ]);

        BulkTramitesContactValidator.Validate(BulkTramitesTemplateType.Matricula, fila)
            .Should().Be("datos_contacto_incompletos:propietario_1");
    }

    [Fact]
    public void BloqueSinDocumento_SeIgnoraAunqueVengaVacio()
    {
        var fila = Fila(
        [
            ("propietario_1_numero_documento", "1"), .. Contacto("propietario_1"),
            ("propietario_2_email", "solo@correo.co"),
        ]);

        BulkTramitesContactValidator.Validate(BulkTramitesTemplateType.Matricula, fila).Should().BeNull();
    }

    [Fact]
    public void OtrosTramites_ActorSinCelular_MarcaLaFila()
    {
        var fila = Fila(
        [
            ("tipo_tramite", "CAMBIO_COLOR"),
            ("actor_1_numero_documento", "1"),
            ("actor_1_email", "a@b.co"),
            ("actor_1_ciudad", "Bogotá"),
            ("actor_1_direccion", "Calle 1"),
        ]);

        BulkTramitesContactValidator.Validate(BulkTramitesTemplateType.Otros, fila)
            .Should().Be("datos_contacto_incompletos:actor_1");
    }

    [Fact]
    public void FilaSinActores_NoError()
    {
        var fila = Fila(("placa", "ABC123"));

        BulkTramitesContactValidator.Validate(BulkTramitesTemplateType.Traspaso, fila).Should().BeNull();
    }
}
