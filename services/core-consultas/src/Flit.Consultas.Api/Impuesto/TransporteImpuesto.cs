using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Consultas.Api.Impuesto;

internal enum EstadoTrabajoExterno { EnCola, Ejecutando, Exitoso, Parcial, Fallido, NoEncontrado, Desconocido }

/// <summary>POST /consultas. <see cref="IdExterno"/> lleno = aceptado. <see cref="CodigoError"/> = error.code de la fuente, solo para clasificar.</summary>
internal sealed record EnvioImpuesto(string? IdExterno, FalloProveedor? Fallo, string? CodigoError);

/// <summary>GET /consultas/{id}. <see cref="Cuerpo"/> = JSON completo solo con Exitoso/Parcial/Fallido.</summary>
internal sealed record EstadoImpuesto(EstadoTrabajoExterno Estado, string? Cuerpo, FalloProveedor? Fallo, string? CodigoError);

/// <summary>GET /files/{fileId}/download.</summary>
internal sealed record ArchivoImpuesto(byte[]? Contenido, string? ContentType, FalloProveedor? Fallo, bool NoEncontrado);

internal interface ITransporteImpuesto
{
    bool Listo { get; }

    string NombreProveedor { get; }

    Task<EnvioImpuesto> EnviarAsync(string cuerpo, string apiKey, CancellationToken ct);

    Task<EstadoImpuesto> ConsultarAsync(string idExterno, string apiKey, CancellationToken ct);

    Task<ArchivoImpuesto> DescargarAsync(string archivoId, string apiKey, CancellationToken ct);
}

internal enum MotivoSinCredencial { Ninguno, SinConfigurar, ProductoApagado }

internal sealed record CredencialImpuesto(bool Disponible, string? ApiKey, long Version, MotivoSinCredencial Motivo)
{
    public override string ToString() => $"CredencialImpuesto {{ Disponible = {Disponible}, Version = {Version}, Motivo = {Motivo} }}";
}

internal interface IFuenteCredencialImpuesto
{
    Task<CredencialImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct);
}

/// <summary>Identidad caída o denegada al pedir la llave: se traduce al fallo del chequeo sin llamar a la fuente.</summary>
internal sealed class FalloCredencialImpuestoException(FalloProveedor fallo, string clave) : Exception
{
    public FalloProveedor Fallo { get; } = fallo;

    public string Clave { get; } = clave;
}

internal sealed record SeguimientoImpuesto(Guid TrabajoId, string? IdExterno, DateTimeOffset? EnviadoEn, string Estado);

internal interface ISeguimientoImpuesto
{
    Task<SeguimientoImpuesto?> LeerAsync(Guid trabajoId, CancellationToken ct);

    /// <summary>INSERT … ON CONFLICT (trabajo_id) DO NOTHING y lee la fila.</summary>
    Task<SeguimientoImpuesto> ReservarAsync(Guid trabajoId, Guid tenantId, string departamento, CancellationToken ct);

    Task GuardarIdExternoAsync(Guid trabajoId, string idExterno, DateTimeOffset enviadoEn, CancellationToken ct);

    Task OlvidarIdExternoAsync(Guid trabajoId, CancellationToken ct);

    Task CerrarAsync(Guid trabajoId, string estado, CancellationToken ct);
}

/// <summary>Intervalos del sondeo y presupuesto total desde <c>submitted_at</c>.</summary>
internal sealed record PlanSondeoImpuesto(TimeSpan Presupuesto, Func<int, TimeSpan> Intervalo)
{
    public static PlanSondeoImpuesto Real(int presupuestoSegundos) =>
        new(TimeSpan.FromSeconds(presupuestoSegundos), n => n < 2
            ? TimeSpan.FromSeconds(5)
            : n == 2 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(15));

    public static PlanSondeoImpuesto Simulado(int presupuestoSegundos) =>
        new(TimeSpan.FromSeconds(presupuestoSegundos), _ => TimeSpan.Zero);
}
