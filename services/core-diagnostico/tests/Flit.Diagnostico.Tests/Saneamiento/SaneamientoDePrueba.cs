using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Dictamen;
using Flit.Diagnostico.Domain.Saneamiento;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Tablero;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;
using EventoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.EventoSaneamiento;

namespace Flit.Diagnostico.Tests.Saneamiento;

/// <summary>
/// Ayudas del saneamiento del cliente (HU #14576): rutas, tokens de la compañía y siembra directa por el contexto de la empresa.
/// Placas ficticias del rango SAN###.
/// </summary>
/// <example>
/// <code>
/// var lote = await SaneamientoDePrueba.LoteAsync(empresa, ["SAN101"], ct);
/// var marca = await api.Cliente(SaneamientoDePrueba.Token(api, empresa.Id)).PutAsync(SaneamientoDePrueba.Ruta(lote.Id, lote.Vehiculos[0].Id), null, ct);
/// </code>
/// </example>
internal static class SaneamientoDePrueba
{
    public const string RutaMasivo = Program.Prefijo + "/saneamientos/solicitudes";

    public static string Ruta(Guid lote, Guid vehiculo) => $"{Program.Prefijo}{DiagnosticosEndpoints.Ruta}/{lote}/vehiculos/{vehiculo}/saneamiento";

    /// <summary>Usuario de la compañía con <c>diagnosticos.ver</c> y, si <paramref name="solicitar"/>, <c>saneamiento.solicitar</c>.</summary>
    public static string Token(ApiDePrueba api, Guid empresa, bool solicitar = true, string rol = "usuario_diagnostico", Guid? usuario = null) =>
        api.Token(
            empresa: empresa,
            permisos: solicitar ? [Permisos.DiagnosticosVer, Permisos.SaneamientoSolicitar] : [Permisos.DiagnosticosVer],
            roles: [rol],
            sujeto: usuario?.ToString());

    /// <summary>Un lote con un vehículo terminado (RUNT y SIMIT completados) por placa, salvo las de <paramref name="abiertas"/>.</summary>
    public static Task<LoteSembrado> LoteAsync(
        EmpresaDePrueba empresa, string[] placas, CancellationToken ct, string[]? abiertas = null, ModoDiagnostico? modo = null,
        string? tipoTramiteCodigo = null, Guid? creador = null) =>
        Tablero.Tablero.SembrarAsync(empresa, ct, version: 5, creador: creador, modo: modo, tipoTramiteCodigo: tipoTramiteCodigo, vehiculos:
        [
            .. placas.Select(p => new VehiculoSembrado(p,
            [
                new(Fuente.Runt, EstadoCelda.Completado, 1),
                new(Fuente.SimitVehiculo, abiertas?.Contains(p) == true ? EstadoCelda.Error : EstadoCelda.Completado, 2),
            ])),
        ]);

    /// <summary>Siembra un saneamiento en <paramref name="estado"/> con los campos que exigen los CHECK y la columna del vehículo.</summary>
    public static async Task<SaneamientoEntidad> SembrarAsync(
        EmpresaDePrueba empresa, Guid lote, Guid vehiculo, EstadoSaneamiento estado, CancellationToken ct, Guid? solicitante = null)
    {
        var ahora = DateTimeOffset.UtcNow.AddMinutes(-30);
        var flit = Guid.NewGuid();
        var tomado = estado is EstadoSaneamiento.EnGestion or EstadoSaneamiento.Saneado or EstadoSaneamiento.NoProcede;
        var cerrado = estado is EstadoSaneamiento.Saneado or EstadoSaneamiento.NoProcede;
        var saneamiento = new SaneamientoEntidad
        {
            TenantId = empresa.Id,
            DiagnosticoId = lote,
            VehiculoId = vehiculo,
            Estado = estado,
            SolicitadoPor = solicitante ?? Guid.NewGuid(),
            SolicitadoEn = ahora,
            TomadoPor = tomado ? flit : null,
            TomadoEn = tomado ? ahora.AddMinutes(5) : null,
            CerradoPor = cerrado ? flit : null,
            CerradoEn = cerrado ? ahora.AddMinutes(10) : null,
            MotivoCierre = estado == EstadoSaneamiento.NoProcede ? "No procede por pago externo." : null,
            AnuladoPor = estado == EstadoSaneamiento.Anulado ? Guid.NewGuid() : null,
            AnuladoEn = estado == EstadoSaneamiento.Anulado ? ahora.AddMinutes(1) : null,
        };
        await using var db = empresa.Db();
        db.Add(saneamiento);
        db.Add(new EventoEntidad
        {
            TenantId = empresa.Id,
            SaneamientoId = saneamiento.Id,
            Evento = TipoEventoSaneamiento.Solicitado,
            EstadoNuevo = EstadoSaneamiento.Solicitado,
            UsuarioId = saneamiento.SolicitadoPor,
            OcurridoEn = ahora,
        });
        await db.SaveChangesAsync(ct);
        await db.Set<VehiculoDiagnostico>().Where(v => v.Id == vehiculo)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.SaneamientoEstado, estado == EstadoSaneamiento.Anulado ? null : estado), ct);
        return saneamiento;
    }

    /// <summary>Dictamen vigente del vehículo con el veredicto dado.</summary>
    public static async Task DictamenAsync(EmpresaDePrueba empresa, Guid vehiculo, string veredicto, CancellationToken ct)
    {
        await using var db = empresa.Db();
        db.Add(new DictamenVehiculo
        {
            VehiculoId = vehiculo,
            Version = 1,
            Vigente = true,
            Veredicto = veredicto,
            Alertamientos = "[]",
            CatalogoVersion = CatalogoReglasDictamen.Version,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
            CreadoEn = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta, CancellationToken ct) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct);

    public static async Task<string?> CodigoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        using var json = JsonDocument.Parse(texto);
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
