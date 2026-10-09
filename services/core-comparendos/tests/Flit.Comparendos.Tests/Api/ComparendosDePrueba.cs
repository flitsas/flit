using System.Net.Http.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Atajos para las pruebas que necesitan una empresa con capacidades y comparendos ya ingresados.</summary>
public static class ComparendosDePrueba
{
    public static async Task<Guid> EmpresaAsync(ApiConFuente api, bool dp = false, bool consulta = false, bool cambio = false, bool ocr = false, bool reportes = false,
        string integracion = "Manual", bool requiereRl = true, string conjunto = "Flit")
    {
        var empresa = Guid.NewGuid();
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await super.PostAsJsonAsync("/api/v1/comparendos/admin/empresas", new { id = empresa, nombre = "Gestora de prueba" })).EnsureSuccessStatusCode();
        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", new
        {
            hasRightPetitionModule = dp, allowsContravenerConsultation = consulta, allowsContravenerChange = cambio, hasOcrAccess = ocr, hasReportesModule = reportes,
            tipoIntegracionConductor = integracion, conjuntoPlantillas = conjunto, requiereRepresentanteLegal = requiereRl, empresaNombre = "Gestora de prueba",
        })).EnsureSuccessStatusCode();
        return empresa;
    }

    /// <summary>Ingresa los comparendos por SIMIT con un NIT nuevo de la empresa y devuelve sus ids por número normalizado.</summary>
    public static async Task<Dictionary<string, Guid>> IngresarAsync(ApiConFuente api, Guid empresa, params ItemFuente[] items)
    {
        var nit = $"8{Random.Shared.Next(10000000, 99999999)}";
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = nit, nombreTitular = "TITULAR DE PRUEBA S.A.S." })).EnsureSuccessStatusCode();
        api.Fuente.Respuestas[$"SimitNit:{nit}"] = () => RespuestaFuente.Exitosa(items);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        return await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa).ToDictionaryAsync(c => c.NumeroNormalizado, c => c.Id));
    }

    public static ItemFuente Item(string numero, string placa = "ABC124", DateOnly? notificacion = null, bool fotomulta = true, decimal valor = 604100m) => new()
    {
        Numero = numero, Placa = placa, FechaImposicion = new DateOnly(2026, 9, 28), FechaNotificacion = notificacion ?? new DateOnly(2026, 10, 1), EsFotomulta = fotomulta,
        Secretaria = "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", ValorTotal = valor, CodigoInfraccion = "C29", DescripcionInfraccion = "Exceso de velocidad",
        TipoDocumentoInfractor = TipoDocumento.NIT, NumeroDocumentoInfractor = "900123456", NombreInfractor = "TITULAR DE PRUEBA S.A.S.",
    };
}
