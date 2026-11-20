using System.Net.Http.Json;
using Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Tests.Api;

/// <summary>Atajos para las pruebas que necesitan una empresa con capacidades y comparendos ya ingresados.</summary>
public static class ComparendosDePrueba
{
    /// <summary>
    /// NIT de la gestora de prueba: es el propietario que trae <see cref="Item"/>, así la gestora es dueña de sus vehículos y
    /// su representante puede firmar a nombre del propietario (hallazgo B9-1).
    /// </summary>
    public const string NitGestora = "900123456";

    public static async Task<Guid> EmpresaAsync(ApiConFuente api, bool dp = false, bool consulta = false, bool cambio = false, bool ocr = false, bool reportes = false,
        string integracion = "Manual", bool requiereRl = true, string conjunto = "Flit")
    {
        var empresa = Guid.NewGuid();
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        (await api.RegistrarEmpresaAsync(super, empresa, "Gestora de prueba", NitGestora)).EnsureSuccessStatusCode();
        (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/capacidades", new
        {
            hasRightPetitionModule = dp, allowsContravenerConsultation = consulta, allowsContravenerChange = cambio, hasOcrAccess = ocr, hasReportesModule = reportes,
            tipoIntegracionConductor = integracion, conjuntoPlantillas = conjunto, requiereRepresentanteLegal = requiereRl,
        })).EnsureSuccessStatusCode();
        await ConsultasActivasAsync(api, empresa);
        return empresa;
    }

    /// <summary>HU #13521: los cuatro canales encendidos con la fuente de prueba y la configuración activada.</summary>
    public static async Task ConsultasActivasAsync(ApiConFuente api, Guid empresa, string proveedor = "prueba")
    {
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        foreach (var canal in new[] { "SimitNit", "SimitPlaca", "MunicipalNit", "MunicipalPlaca" })
        {
            (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/canales/{canal}", new
            {
                encendido = true, proveedor = canal == "SimitNit" ? proveedor : "prueba", modo = "HorasFijas", horas = new[] { "07:00" }, soloDiasHabiles = false,
            })).EnsureSuccessStatusCode();
        }

        (await super.PostAsync($"/api/v1/comparendos/admin/empresas/{empresa}/consultas/activar", null)).EnsureSuccessStatusCode();
    }

    /// <summary>Ingresa los comparendos por SIMIT con un NIT nuevo de la empresa y devuelve sus ids por número normalizado.</summary>
    public static Task<Dictionary<string, Guid>> IngresarAsync(ApiConFuente api, Guid empresa, params ItemFuente[] items) =>
        IngresarAsync(api, empresa, propietarioEsElNit: false, items);

    /// <summary>
    /// Como <see cref="IngresarAsync(ApiConFuente, Guid, ItemFuente[])"/>; con <paramref name="propietarioEsElNit"/> la fuente
    /// devuelve como infractor el NIT consultado, como el SIMIT real (el propietario es la empresa del NIT).
    /// </summary>
    public static async Task<Dictionary<string, Guid>> IngresarAsync(ApiConFuente api, Guid empresa, bool propietarioEsElNit, params ItemFuente[] items)
    {
        var nit = $"8{Random.Shared.Next(10000000, 99999999)}";
        if (propietarioEsElNit)
            items = [.. items.Select(i => i with { NumeroDocumentoInfractor = nit })];
        (await api.Cliente(new UsuarioDePrueba(empresa, [Permisos.MonitoreoGestionar])).PostAsJsonAsync("/api/v1/comparendos/monitoreo/nits", new { numero = nit, nombreTitular = "TITULAR DE PRUEBA S.A.S." })).EnsureSuccessStatusCode();
        api.Fuente.Respuestas[$"SimitNit:{nit}"] = () => RespuestaFuente.Exitosa(items);
        await api.CicloAsync(CanalIngesta.SimitNit, empresa);
        return await api.EnDbAsync(db => db.Comparendos.Where(c => c.TenantId == empresa).ToDictionaryAsync(c => c.NumeroNormalizado, c => c.Id));
    }

    /// <summary>HU #13712: el SuperAdmin marca (o desmarca) todos los NIT de la empresa para consultar la API de contratos.</summary>
    public static async Task ConsultaPorApiDeLosNitsAsync(ApiConFuente api, Guid empresa, bool activo = true)
    {
        var super = api.Cliente(new UsuarioDePrueba(Guid.NewGuid(), [], SuperAdmin: true));
        foreach (var nit in await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.Where(n => n.TenantId == empresa).Select(n => n.Id).ToListAsync()))
            (await super.PutAsJsonAsync($"/api/v1/comparendos/admin/empresas/{empresa}/nits/{nit}/consulta-contrato", new { activo })).EnsureSuccessStatusCode();
    }

    public static ItemFuente Item(string numero, string placa = "ABC124", DateOnly? notificacion = null, bool fotomulta = true, decimal valor = 604100m) => new()
    {
        Numero = numero, Placa = placa, FechaImposicion = new DateOnly(2026, 9, 28), FechaNotificacion = notificacion ?? new DateOnly(2026, 10, 1), EsFotomulta = fotomulta,
        Secretaria = "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA", ValorTotal = valor, CodigoInfraccion = "C29", DescripcionInfraccion = "Exceso de velocidad",
        TipoDocumentoInfractor = TipoDocumento.NIT, NumeroDocumentoInfractor = "900123456", NombreInfractor = "TITULAR DE PRUEBA S.A.S.",
    };

    /// <summary>Representante legal del NIT vigilado de la empresa (el que firma por ese propietario).</summary>
    public static async Task RepresentanteDelNitAsync(ApiConFuente api, HttpClient cliente, Guid empresa, CancellationToken ct)
    {
        var nitId = await api.EnDbAsync(db => db.IdentificacionesMonitoreadas.Where(n => n.TenantId == empresa).Select(n => n.Id).SingleAsync(ct));
        (await cliente.PostAsJsonAsync("/api/v1/comparendos/representantes", new
        {
            alcance = "Identificacion", identificacionMonitoreadaId = nitId, tipoDocumento = "CC", numeroDocumento = "52123456", nombres = "María Gómez", correo = "rl@gestora.test",
            cargo = "Representante legal", permisoEnvioDp = true, permisoCambioContraventor = true,
        }, ct)).EnsureSuccessStatusCode();
    }
}
