using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// Lo que el cuerpo de Verifik agrega al resumen: licencias con organismo y suspensión, certificados, trámites,
/// y del SIMIT las multas y los acuerdos. Sin nombres ni documento.
/// </summary>
public sealed class ConductorYSimitVisibleTests
{
    [Fact]
    public void Conductor_completa_licencia_certificado_y_tramite_sin_el_nombre()
    {
        using var doc = JsonDocument.Parse("""
            {
              "data": {
                "fullName": "PERSONA FICTICIA",
                "documentNumber": "1000000001",
                "inscriptionDate": "04/11/2009",
                "infractions": { "tieneMultas": "SI", "nroPazYSalvo": null },
                "licenses": [
                  { "category": "C1", "status": "VENCIDA", "dueDate": "04/04/2019", "licenceNumber": "LIC-0001", "otExpide": "ORGANISMO DE PRUEBA", "expeditionDate": "04/04/2016", "restrictions": "LENTES", "startDateSuspension": "01/01/2020", "endDateSuspension": "01/06/2020" }
                ],
                "aptitudeCertificates": [
                  { "categoria": "C1", "estadoDocumento": "UTILIZADO", "numeroCertificadoAptitud": 1350, "tipoCertificado": "CERTIFICADO CONDUCTOR", "nombreCea": "CEA DE PRUEBA", "idPersona": "1000000001" }
                ],
                "requests": [
                  { "nombreTramite": "TRAMITE DE PRUEBA", "estadoSolicitud": "AUTORIZADA", "estadoTramite": "APROBADO", "fechaSolicitud": "04/04/2016", "entidad": "ORGANISMO DE PRUEBA", "registro": "RNC", "identificador": "C 1000000001" }
                ]
              }
            }
            """);
        var actual = new ResultadoRuntPersonaDetalle(true, "ACTIVO", []);

        var completo = LicenciasConductor.Completar(actual, doc.RootElement);

        completo.FechaInscripcion.Should().Be("04/11/2009");
        completo.TieneMultas.Should().Be("SI");
        completo.Licencias.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LicenciaDetalle("C1", "VENCIDA", "04/04/2019", "LIC-0001", "04/04/2016", "ORGANISMO DE PRUEBA", "LENTES", "01/01/2020", "01/06/2020"));
        completo.Certificados.Should().ContainSingle().Which.Entidad.Should().Be("CEA DE PRUEBA");
        completo.Solicitudes.Should().ContainSingle().Which.Tramite.Should().Be("TRAMITE DE PRUEBA");
        JsonSerializer.Serialize(completo).Should().NotContain("PERSONA FICTICIA").And.NotContain("1000000001");
    }

    [Fact]
    public void Simit_toma_multas_y_acuerdos_sin_el_infractor()
    {
        using var doc = JsonDocument.Parse("""
            {
              "data": {
                "comparendos": [],
                "pazSalvo": false,
                "suspendida": false,
                "cancelada": false,
                "totalGeneral": "1000",
                "totalMultasPagar": "1",
                "multas": [
                  { "numeroComparendo": "CMP-0001", "fechaComparendo": "31/07/2016 00:00:00", "valor": "100", "valorPagar": "150", "estadoCartera": "Cobro coactivo", "organismoTransito": "ORGANISMO DE PRUEBA", "infracciones": [{ "codigoInfraccion": "C24" }], "infractor": { "nombre": "NOMBRE OCULTO", "numeroDocumento": "1000000001" } }
                ],
                "acuerdosPago": [
                  { "estado": "Acuerdo de pago", "cantCuotasAbonadas": "13", "cantCuotasPendientes": "0", "pendiente": "0", "fechaResolucion": "27/10/2025 00:00:00", "infractor": { "nombre": "NOMBRE OCULTO" } }
                ]
              }
            }
            """);
        var actual = new ResultadoSimitDetalle(null, 0, [], false);

        var completo = SimitVisible.Completar(actual, doc.RootElement);

        completo.TotalGeneral.Should().Be("1000");
        completo.PazYSalvo.Should().BeFalse();
        completo.Multas.Should().ContainSingle().Which.Codigos.Should().Be("C24");
        completo.Acuerdos.Should().ContainSingle().Which.Estado.Should().Be("Acuerdo de pago");
        JsonSerializer.Serialize(completo).Should().NotContain("NOMBRE OCULTO").And.NotContain("1000000001");
    }
}
