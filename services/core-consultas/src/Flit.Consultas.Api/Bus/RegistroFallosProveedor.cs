using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Colector del último <see cref="ConsultationResult.Fallo"/> del proveedor, acotado a UN trabajo del bus del Diagnóstico
/// (resolvedor de errores, solo Diagnóstico). <see cref="EjecutorTrabajo"/> lo abre alrededor de la ejecución;
/// <see cref="Grpc.ConsultasGrpcService"/> anota cada resultado que recibe del proveedor o de la cadena, y gana el último (si el
/// respaldo respondió, no queda motivo). Vive en un <see cref="AsyncLocal{T}"/>: dos trabajos concurrentes no se cruzan, y fuera
/// de un trabajo (las llamadas gRPC de Trámites y Comparendos) <see cref="Anotar"/> es un no-op y nadie lee nada: la respuesta gRPC
/// no cambia.
/// </summary>
/// <example>
/// <code>
/// using (RegistroFallosProveedor.Abrir())
/// {
///     var respuesta = await consultas.ConsultarMultas(request, caller, ct);
///     var motivo = RegistroFallosProveedor.Motivo(); // "CREDENCIALES" con un 403 del proveedor
/// }
/// </code>
/// </example>
internal static class RegistroFallosProveedor
{
    private static readonly AsyncLocal<Caja?> Actual = new();

    /// <summary>Abre el colector del trabajo en curso; al desecharlo se restaura el anterior (ninguno, fuera del bus).</summary>
    public static IDisposable Abrir()
    {
        var anterior = Actual.Value;
        Actual.Value = new Caja();
        return new Cierre(anterior);
    }

    /// <summary>Anota el fallo de <paramref name="resultado"/> (null si respondió) en el trabajo en curso; sin trabajo, nada.</summary>
    public static void Anotar(ConsultationResult resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        if (Actual.Value is { } caja)
            caja.Fallo = resultado.Fallo;
    }

    /// <summary>El código del último fallo anotado en el trabajo en curso (<see cref="FalloProveedor.Codigo"/>), o null.</summary>
    public static string? Motivo() => Actual.Value?.Fallo?.Codigo;

    private sealed class Caja
    {
        public FalloProveedor? Fallo { get; set; }
    }

    private sealed class Cierre(Caja? anterior) : IDisposable
    {
        public void Dispose() => Actual.Value = anterior;
    }
}
