namespace Flit.Consultas.Api.Bus;

/// <summary>
/// Configuración de los consumidores de trabajos del diagnóstico (ADR-0075), sección <see cref="Seccion"/>.
/// <see cref="TiempoMaximo"/> es el tope de UNA ejecución (el deadline que hoy pone Diagnóstico a su llamada gRPC, 90 s por
/// defecto): vencido, el resultado es ERROR <c>DeadlineExceeded</c>.
/// </summary>
internal sealed class OpcionesConsumidoresDiagnostico
{
    public const string Seccion = "Consultas:Diagnostico";

    public const int TiempoMaximoPorDefectoSegundos = 90;

    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromSeconds(TiempoMaximoPorDefectoSegundos);

    public static OpcionesConsumidoresDiagnostico Desde(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var segundos = configuration.GetValue($"{Seccion}:TiempoMaximoSegundos", TiempoMaximoPorDefectoSegundos);
        return new OpcionesConsumidoresDiagnostico { TiempoMaximo = TimeSpan.FromSeconds(segundos) };
    }
}
