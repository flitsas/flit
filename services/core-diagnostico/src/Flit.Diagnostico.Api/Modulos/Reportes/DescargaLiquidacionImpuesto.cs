using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// PDF de liquidación de impuesto por vigencia o consolidada (D5). El <c>fileId</c> sale del payload de la celda
/// COMPLETADO; el cliente no lo envía. Llama a <c>DescargarLiquidacionImpuesto</c> con el tenant de la celda.
/// </summary>
internal sealed partial class DescargaLiquidacionImpuesto(
    DiagnosticoDb db, UsuarioActual usuario, IServiceProvider servicios, ILogger<DescargaLiquidacionImpuesto> log)
{
    public const string CodigoNoDisponible = "LIQUIDACION_NO_DISPONIBLE";
    public const string CodigoNoEncontrada = "LIQUIDACION_NO_ENCONTRADA";
    public const string CodigoCredencialNoConfigurada = "CREDENCIAL_NO_CONFIGURADA";
    public const string CodigoCredencialRechazada = "CREDENCIAL_RECHAZADA";
    public const string CodigoArchivoInvalido = "ARCHIVO_INVALIDO";
    public const string CodigoFuenteNoDisponible = "FUENTE_NO_DISPONIBLE";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    public async Task<ArchivoGenerado> ObtenerAsync(Guid diagnosticoId, Guid vehiculoId, string vigencia, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vigencia);
        if (!EsVigencia(vigencia))
            throw new ReglaException("VIGENCIA_INVALIDA", "vigencia debe ser un año AAAA o consolidada.", StatusCodes.Status400BadRequest) { Campo = "vigencia" };

        var cabecera = await AccesoLote.LeerVisibleAsync(db, usuario, diagnosticoId, seguir: false, ct).ConfigureAwait(false);
        var vehiculo = await db.Set<VehiculoDiagnostico>().AsNoTracking()
            .Where(v => v.Id == vehiculoId && v.DiagnosticoId == cabecera.Id)
            .Select(v => new { v.Placa, v.Vin, v.IdentificadorNorm })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El vehículo");

        var celda = await db.Set<CeldaConsulta>().AsNoTracking()
            .Where(c => c.DiagnosticoId == cabecera.Id && c.VehiculoId == vehiculoId && c.Fuente == Fuente.ImpuestoDepartamental)
            .Select(c => new { c.TenantId, c.Estado, c.Payload })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("La celda");

        if (celda.Estado != EstadoCelda.Completado || string.IsNullOrWhiteSpace(celda.Payload))
            throw ReglaException.Conflicto(CodigoNoDisponible, "La liquidación del impuesto no está disponible.");

        var archivoId = FileIdDelPayload(celda.Payload, vigencia)
            ?? throw ReglaException.Conflicto(CodigoNoDisponible, "La liquidación del impuesto no está disponible.");

        var consultas = servicios.GetService<ClienteConsultas>()
            ?? throw new ReglaException(CodigoFuenteNoDisponible, "La fuente del impuesto no está disponible.", StatusCodes.Status503ServiceUnavailable);

        var deadline = DateTime.UtcNow.Add(Deadline);
        var respuesta = await consultas.LlamarAsync(
            celda.TenantId,
            (c, o) => c.DescargarLiquidacionImpuestoAsync(
                new DescargarLiquidacionImpuestoRequest { ArchivoId = archivoId },
                o.WithDeadline(deadline)),
            ct).ConfigureAwait(false);

        if (respuesta.Error is not null)
            throw MapearError(respuesta.Codigo, respuesta.Reason);

        var bytes = respuesta.Respuesta!.Contenido.ToByteArray();
        if (bytes.Length == 0 || !FirmasArchivo.EsPdf(bytes))
            throw new ReglaException(CodigoArchivoInvalido, "La fuente no devolvió un PDF válido.", StatusCodes.Status502BadGateway);

        var placa = vehiculo.Placa ?? vehiculo.Vin ?? vehiculo.IdentificadorNorm;
        var nombre = $"impuesto-{diagnosticoId.ToString("N")[..8]}-{placa}-{vigencia}.pdf";
        Descargada(log, usuario.Id, cabecera.TenantId, diagnosticoId, vehiculoId, vigencia);
        return new ArchivoGenerado(bytes, nombre, "application/pdf");
    }

    private static string? FileIdDelPayload(string payload, string vigencia)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var raiz = doc.RootElement;
            if (string.Equals(vigencia, "consolidada", StringComparison.OrdinalIgnoreCase))
                return Texto(raiz, "liquidacion_consolidada_id");

            if (!int.TryParse(vigencia, out var anio) || !raiz.TryGetProperty("vigencias", out var lista) || lista.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var v in lista.EnumerateArray())
            {
                if (v.TryGetProperty("anio", out var a) && a.TryGetInt32(out var n) && n == anio)
                    return Texto(v, "liquidacion_id");
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement e, string nombre)
    {
        if (!e.TryGetProperty(nombre, out var valor) || valor.ValueKind != JsonValueKind.String)
            return null;
        var limpio = valor.GetString()?.Trim();
        return string.IsNullOrEmpty(limpio) ? null : limpio;
    }

    private static bool EsVigencia(string vigencia) =>
        string.Equals(vigencia, "consolidada", StringComparison.OrdinalIgnoreCase) || Anio().IsMatch(vigencia);

    private static ReglaException MapearError(StatusCode? codigo, string? reason) => codigo switch
    {
        StatusCode.NotFound => new ReglaException(CodigoNoEncontrada, "La liquidación no se encontró en la fuente.", StatusCodes.Status404NotFound),
        StatusCode.FailedPrecondition => ReglaException.Conflicto(
            string.Equals(reason, CodigoCredencialNoConfigurada, StringComparison.Ordinal) ? CodigoCredencialNoConfigurada : CodigoCredencialNoConfigurada,
            "Credencial de impuesto no configurada para la compañía."),
        StatusCode.PermissionDenied => new ReglaException(CodigoCredencialRechazada, "La fuente rechazó la credencial de impuesto.", StatusCodes.Status502BadGateway),
        StatusCode.ResourceExhausted or StatusCode.Internal =>
            new ReglaException(CodigoArchivoInvalido, "La fuente no devolvió un PDF válido.", StatusCodes.Status502BadGateway),
        StatusCode.Unavailable or StatusCode.DeadlineExceeded =>
            new ReglaException(CodigoFuenteNoDisponible, "La fuente del impuesto no está disponible.", StatusCodes.Status503ServiceUnavailable),
        _ => new ReglaException(CodigoFuenteNoDisponible, "La fuente del impuesto no está disponible.", StatusCodes.Status503ServiceUnavailable),
    };

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex Anio();

    [LoggerMessage(EventId = 138651, Level = LogLevel.Information,
        Message = "impuesto.liquidacion.descargada usuario={UsuarioId} empresa={TenantId} diagnostico={DiagnosticoId} vehiculo={VehiculoId} vigencia={Vigencia}")]
    private static partial void Descargada(ILogger logger, Guid usuarioId, Guid tenantId, Guid diagnosticoId, Guid vehiculoId, string vigencia);
}
