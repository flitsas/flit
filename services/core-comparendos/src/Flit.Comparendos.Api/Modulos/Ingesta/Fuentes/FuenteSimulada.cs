using System.Security.Cryptography;
using System.Text;
using Flit.Comparendos.Api.Plataforma;
using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Ingesta.Fuentes;

/// <summary>
/// Fuente simulada para DEV y demostraciones mientras no estén conectadas SmartCloud y la API municipal: para cada clave
/// devuelve siempre los mismos comparendos (de 0 a 4, según un hash de la clave), con secretarías reales del catálogo,
/// fotomultas y comparendos físicos, y fechas relativas a hoy para que el semáforo muestre todos los estados. Las
/// claves que terminan en «000» simulan una fuente caída (HTTP 503) para probar la regla de error [Excel C3].
/// </summary>
public sealed class FuenteSimulada(Reloj reloj) : IFuenteComparendos
{
    private static readonly string[] Secretarias =
    [
        "SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA",
        "SECRETARIA DE MOVILIDAD DE MEDELLIN",
        "SECRETARIA DE MOVILIDAD DE SANTIAGO DE CALI",
        "SECRETARIA DE TRANSITO Y TRANSPORTE DE BARRANQUILLA",
        "SECRETARIA DE TRANSITO DE SOACHA",
    ];

    private static readonly (string Codigo, string Descripcion, decimal Valor)[] Infracciones =
    [
        ("C29", "Conducir un vehículo a velocidad superior a la máxima permitida.", 604100m),
        ("C14", "Transitar por sitios restringidos o en horas prohibidas por la autoridad competente (pico y placa).", 604100m),
        ("D04", "No detenerse ante una luz roja o amarilla de semáforo.", 1208200m),
        ("C02", "Estacionar un vehículo en sitios prohibidos.", 604100m),
        ("C35", "No realizar la revisión técnico-mecánica en el plazo legal.", 604100m),
    ];

    public const string Clave = "simulado";

    public Task<IReadOnlyList<ProveedorFuente>> ProveedoresAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProveedorFuente>>([new ProveedorFuente(Clave, "Simulado (solo DEV, sin core-consultas)",
            [CanalIngesta.SimitNit, CanalIngesta.SimitPlaca, CanalIngesta.MunicipalNit, CanalIngesta.MunicipalPlaca], true, null)]);

    public Task<RespuestaFuente> ConsultarAsync(ClaveConsulta clave, string proveedor, Guid empresa, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clave);
        if (clave.Clave.EndsWith("000", StringComparison.Ordinal))
            return Task.FromResult(RespuestaFuente.Error(TipoErrorFuente.HttpNoExitoso, "Fuente simulada no disponible (503).", 503, 40));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{clave.Canal}|{clave.Clave}"));
        var cantidad = hash[0] % 5;
        var hoy = reloj.Hoy;
        var porPlaca = clave.Canal is CanalIngesta.SimitPlaca or CanalIngesta.MunicipalPlaca;
        var municipal = clave.Canal is CanalIngesta.MunicipalNit or CanalIngesta.MunicipalPlaca;
        var items = new List<ItemFuente>();
        for (var i = 0; i < cantidad; i++)
        {
            var b = hash[(i * 5) + 1];
            var infraccion = Infracciones[b % Infracciones.Length];
            var fotomulta = b % 4 != 0;
            var diasAtras = new[] { 2, 6, 14, 25, 45, 120, 400, 1200 }[hash[(i * 5) + 2] % 8];
            var imposicion = hoy.AddDays(-diasAtras - (fotomulta ? 5 : 0));
            var notificacion = fotomulta ? (hash[(i * 5) + 3] % 6 == 0 ? (DateOnly?)null : hoy.AddDays(-diasAtras)) : imposicion;
            var placa = porPlaca ? clave.Clave : $"{(char)('A' + (hash[(i * 5) + 4] % 26))}{(char)('A' + (b % 26))}{(char)('A' + (hash[i + 20] % 26))}{100 + (hash[i + 25] % 900)}";
            items.Add(new ItemFuente
            {
                Numero = $"{(municipal ? "M" : string.Empty)}{1100100000000 + (BitConverter.ToUInt32(hash, i * 4 % 28) % 899999999)}",
                Placa = placa,
                TipoDocumentoInfractor = porPlaca ? TipoDocumento.NIT : clave.TipoDocumento ?? TipoDocumento.NIT,
                NumeroDocumentoInfractor = porPlaca ? "900123456" : clave.Clave,
                NombreInfractor = "EMPRESA DE PRUEBA S.A.S.",
                CodigoInfraccion = infraccion.Codigo,
                DescripcionInfraccion = infraccion.Descripcion,
                DireccionInfraccion = $"CALLE {10 + (b % 90)} # {b % 50}-{b % 99}",
                FechaImposicion = imposicion,
                HoraImposicion = new TimeOnly(6 + (b % 14), b % 60),
                FechaNotificacion = notificacion,
                EsFotomulta = fotomulta,
                Secretaria = Secretarias[hash[(i * 5) + 2] % Secretarias.Length],
                TipoVehiculo = "AUTOMOVIL",
                ServicioVehiculo = "PARTICULAR",
                ValorTotal = infraccion.Valor,
                EstadoFuente = diasAtras > 300 ? "COBRO COACTIVO" : "PENDIENTE",
            });
        }

        return Task.FromResult(RespuestaFuente.Exitosa(items, 200, 25, $$"""{"simulado":true,"clave":"{{clave.Clave}}","items":{{items.Count}}}"""));
    }
}
