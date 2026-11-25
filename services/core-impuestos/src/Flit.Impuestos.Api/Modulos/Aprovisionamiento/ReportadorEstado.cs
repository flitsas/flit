using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Aprovisionamiento;

/// <summary>
/// Motivos del estado del aprovisionamiento (<c>MotivoAprovisionamiento</c> de <c>impuestos.v1.yaml</c> 1.1.0) con su literal,
/// que es el <c>mensaje</c> que se reporta a la plataforma (el admin de compañías y el backoffice muestran el mismo texto), y la
/// acción del Super Admin que corresponde a cada estado (<c>AccionAprovisionamiento</c>).
/// </summary>
/// <example>
/// <code>
/// var texto = MotivosAprovisionamiento.Literales[MotivosAprovisionamiento.EmpresaYaRegistrada];
/// var accion = MotivosAprovisionamiento.Accion(EstadoAprovisionamiento.RequiereCredenciales); // "CARGAR_CREDENCIALES"
/// </code>
/// </example>
public static class MotivosAprovisionamiento
{
    public const string ReintentoProgramado = "REINTENTO_PROGRAMADO";
    public const string SinWebhookSecret = "SIN_WEBHOOK_SECRET";
    public const string AltaDatosRechazados = "ALTA_DATOS_RECHAZADOS";
    public const string RazonSocialLarga = "RAZON_SOCIAL_LARGA";
    public const string AmbienteNoConfigurado = "AMBIENTE_NO_CONFIGURADO";
    public const string ProveedorInalcanzable = "PROVEEDOR_INALCANZABLE";
    public const string AltaSinConfirmar = "ALTA_SIN_CONFIRMAR";
    public const string EmpresaYaRegistrada = "EMPRESA_YA_REGISTRADA";
    public const string CredencialRechazada = "CREDENCIAL_RECHAZADA";
    public const string NitAusente = "NIT_AUSENTE";
    public const string NitAmbiguo = "NIT_AMBIGUO";
    public const string NitIgualACabeza = "NIT_IGUAL_A_CABEZA";
    public const string ProductoDesactivado = "PRODUCTO_DESACTIVADO";

    /// <summary>Literales exactos de <c>x-flit-etiquetas</c> (≤ 300 caracteres, sin datos de la compañía).</summary>
    public static IReadOnlyDictionary<string, string> Literales { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ReintentoProgramado] = "No se pudo contactar al proveedor; se reintentará automáticamente.",
        [SinWebhookSecret] = "Falta el secreto del webhook: los resultados se consultan al proveedor.",
        [AltaDatosRechazados] = "El proveedor rechazó los datos del alta de la compañía.",
        [RazonSocialLarga] = "La razón social supera los 200 caracteres que admite el proveedor.",
        [AmbienteNoConfigurado] = "La integración con el proveedor no está configurada en este ambiente.",
        [ProveedorInalcanzable] = "No se pudo contactar al proveedor después de varios intentos.",
        [AltaSinConfirmar] = "El proveedor no confirmó el alta: puede haberla recibido. Reintenta para verificar.",
        [EmpresaYaRegistrada] = "La empresa ya existe en el proveedor: rota las credenciales en la consola del API y cárgalas en Impuestos.",
        [CredencialRechazada] = "El proveedor rechazó la API key de la compañía: carga credenciales nuevas en Impuestos.",
        [NitAusente] = "La compañía no tiene NIT.",
        [NitAmbiguo] = "No se puede separar el dígito de verificación del NIT.",
        [NitIgualACabeza] = "El NIT es el mismo de su compañía cabeza.",
        [ProductoDesactivado] = "Impuestos está desactivado para la compañía.",
    };

    /// <summary>¿Es un código del contrato? (el historial puede tener códigos internos, p. ej. <c>CARGA_MANUAL</c> de M06).</summary>
    public static bool EsDelContrato(string? codigo) => codigo is not null && Literales.ContainsKey(codigo);

    /// <summary>Acción del Super Admin para el estado; null si no hay ninguna. La tabla es una sola: la del backoffice (M13).</summary>
    public static string? Accion(EstadoAprovisionamiento estado) => Backoffice.EmpresasBackofficeEndpoints.Accion(estado);

    /// <summary>Motivo de un estado al que se llega sin un motivo propio (reactivar, suspender); null si no tiene.</summary>
    public static string? DelEstado(EstadoAprovisionamiento estado) => estado switch
    {
        EstadoAprovisionamiento.ListoSinWebhook => SinWebhookSecret,
        EstadoAprovisionamiento.Suspendido => ProductoDesactivado,
        _ => null,
    };
}

/// <summary>
/// Reporta el estado del aprovisionamiento de una compañía a la plataforma (plan §1.2 decisión R1, rpc
/// <c>ReportarEstadoAprovisionamiento</c> de M04) cuando la versión de la fila no se ha reportado (<c>reportado_version &lt;
/// version</c>). Manda el estado, el motivo y su literal, la acción, la firma y la versión, en mayúsculas y vacíos cuando no hay.
/// La plataforma es idempotente por versión: <c>aplicado=false</c> con una versión igual o mayor cuenta como reportado. Si
/// Identidad no responde, no lanza: la fila queda pendiente y la siguiente ronda de <see cref="TrabajadorReintentoAlta"/> la
/// vuelve a intentar. Sin datos de la compañía ni secretos en el reporte ni en los logs.
/// </summary>
/// <example>
/// <code>
/// await reportador.ReportarAsync(empresa, ct); // true si quedó reportada en esta llamada
/// </code>
/// </example>
public sealed partial class ReportadorEstado(AlmacenCredenciales almacen, IClienteIdentidadAlta identidad, ILogger<ReportadorEstado> log)
{
    public async Task<bool> ReportarAsync(Guid empresa, CancellationToken ct)
    {
        await using var db = almacen.ContextoDe(empresa);
        var fila = await db.Set<AprovisionamientoEmpresa>().AsNoTracking().SingleOrDefaultAsync(a => a.TenantId == empresa, ct).ConfigureAwait(false);
        if (fila is null || fila.ReportadoVersion >= fila.Version)
            return false;

        var reporte = Construir(fila);
        RespuestaReporte respuesta;
        try
        {
            respuesta = await identidad.ReportarAsync(empresa, reporte, ct).ConfigureAwait(false);
        }
        catch (IdentidadNoDisponibleException ex)
        {
            NoReportado(log, empresa, fila.Version, ex.Message);
            return false;
        }

        if (!respuesta.Aplicado && respuesta.VersionGuardada < fila.Version)
        {
            NoReportado(log, empresa, fila.Version, $"la plataforma respondió la versión {respuesta.VersionGuardada}");
            return false;
        }

        if (respuesta.VersionGuardada > fila.Version)
            VersionMayor(log, empresa, fila.Version, respuesta.VersionGuardada);

        var version = fila.Version;
        await db.Set<AprovisionamientoEmpresa>()
            .Where(a => a.TenantId == empresa && a.ReportadoVersion < version)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReportadoVersion, version), ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>El reporte de la fila: códigos en mayúsculas, el literal del motivo como mensaje y cadenas vacías si no hay.</summary>
    public static ReporteEstadoAprovisionamiento Construir(AprovisionamientoEmpresa fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        var motivo = MotivosAprovisionamiento.EsDelContrato(fila.MotivoCodigo) ? fila.MotivoCodigo! : string.Empty;
        return new ReporteEstadoAprovisionamiento(
            CodigosDeEnum.Codigo(fila.Estado),
            motivo,
            motivo.Length == 0 ? string.Empty : MotivosAprovisionamiento.Literales[motivo],
            MotivosAprovisionamiento.Accion(fila.Estado) ?? string.Empty,
            fila.FirmaConfirmada,
            fila.Version);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se reportó a la plataforma la versión {Version} del aprovisionamiento de la empresa {Empresa} ({Motivo}); se reintenta en la siguiente ronda.")]
    private static partial void NoReportado(ILogger logger, Guid empresa, long version, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La plataforma tiene la versión {Guardada} del aprovisionamiento de la empresa {Empresa}, mayor que la local {Version}.")]
    private static partial void VersionMayor(ILogger logger, Guid empresa, long version, long guardada);
}
