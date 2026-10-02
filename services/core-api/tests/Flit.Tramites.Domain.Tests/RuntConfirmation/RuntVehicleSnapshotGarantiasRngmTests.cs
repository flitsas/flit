using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.RuntConfirmation;

/// <summary>
/// Bug #13203 — el motor de confirmación lee las garantías del crudo. Con el vocabulario del RNGM
/// (<c>entidad</c>, <c>numeroDocumentoEntidad</c>, <c>fechaRegistro</c> dd/MM/yyyy) el parser perdía
/// acreedor y fecha, y el refuerzo <c>GarantiaInscrita</c> decía siempre «no hay garantía inscrita».
/// Datos ficticios.
/// Uso de ejemplo:
/// <code>RuntConfirmationRules.Evaluate(new RuntConfirmationInput("PRENDA_INSCRIPCION", ProcedureFamily.Otros, corte, RuntVehicleSnapshotParser.Parse(json), null, null, null, null))</code>
/// </summary>
public sealed class RuntVehicleSnapshotGarantiasRngmTests
{
    private const string ItemRngm = """
        {
          "idPrenda": "1000001",
          "idVehiculoPrenda": "2000002",
          "fechaRegistro": "30/09/2026",
          "tipoDocumentoEntidad": "NIT",
          "numeroDocumentoEntidad": "900000001",
          "entidad": "BANCO DE PRUEBA S.A.",
          "estado": "Registro de la garantía en el RNGM por parte de RUNT"
        }
        """;

    private const string Kyverum = $$"""
        {
          "ok": true,
          "data": {
            "vehiculo": { "placa": "PRU13A", "estadoAutomotor": "ACTIVO", "gravamenes": "NO", "prendas": "NO", "mostrarSolicitudes": "SI" },
            "garantias": [],
            "garantiasPrendas": [ {{ItemRngm}} ],
            "solicitudes": [
              { "noSolicitud": "900001", "fechaSolicitud": "2026-09-30T10:00:00.000-05:00", "estado": "AUTORIZADA",
                "tramitesRealizados": "TRÁMITE INSCRIPCIÓN ALERTA, ", "entidad": "STRIA DE PRUEBA" }
            ]
          }
        }
        """;

    private const string Verifik = $$"""
        {
          "data": {
            "informacionGeneral": { "noPlaca": "PRU13A", "estadoDelVehiculo": "ACTIVO", "tieneGravamenes": "SI", "prendas": "NO", "mostrarSolicitudes": "SI" },
            "garantiasMobiliarias": [ {{ItemRngm}} ],
            "solicitudes": [
              { "noSolicitud": "900001", "fechaSolicitud": "30/09/2026", "estado": "AUTORIZADA",
                "tramitesRealizados": "TRÁMITE INSCRIPCIÓN ALERTA, ", "entidad": "STRIA DE PRUEBA" }
            ]
          }
        }
        """;

    [Theory]
    [InlineData("kyverum")]
    [InlineData("verifik")]
    public void ShapeRngm_ElSnapshotLeeAcreedorDocumentoYFecha(string proveedor)
    {
        var snap = RuntVehicleSnapshotParser.Parse(proveedor == "kyverum" ? Kyverum : Verifik);

        snap.ProviderHint.Should().Be(proveedor);
        snap.Garantias.Should().ContainSingle().Which.Should().Be(
            new RuntGarantia("BANCO DE PRUEBA S.A.", "900000001", new DateOnly(2026, 9, 30)));
    }

    [Theory]
    [InlineData("kyverum")]
    [InlineData("verifik")]
    public void ShapeRngm_ElRefuerzoGarantiaInscritaCitaLaEntidad(string proveedor)
    {
        var snap = RuntVehicleSnapshotParser.Parse(proveedor == "kyverum" ? Kyverum : Verifik);

        var d = RuntConfirmationRules.Evaluate(new RuntConfirmationInput(
            "PRENDA_INSCRIPCION", ProcedureFamily.Otros, new DateOnly(2026, 9, 29), snap, null, null, null, null));

        d.Reason.Should().Contain("garantía inscrita el").And.Contain("BANCO DE PRUEBA S.A.")
            .And.NotContain("no hay garantía inscrita");
    }

    [Fact]
    public void ShapePrendaActual_SigueTeniendoPrioridadSobreLosAlias()
    {
        const string json = """
            {
              "ok": true,
              "data": {
                "vehiculo": { "placa": "PRU13A" },
                "garantias": [ { "acreedor": "ACREEDOR PRENDA", "entidad": "OTRA ENTIDAD", "numeroDocumentoAcreedor": "900000002",
                                 "numeroDocumentoEntidad": "900000003", "fechaInscripcion": "01/09/2026", "fechaRegistro": "02/09/2026" } ]
              }
            }
            """;

        RuntVehicleSnapshotParser.Parse(json).Garantias.Should().ContainSingle().Which.Should().Be(
            new RuntGarantia("ACREEDOR PRENDA", "900000002", new DateOnly(2026, 9, 1)));
    }
}
