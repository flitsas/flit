namespace Flit.Impuestos.Api.Modulos.Conciliacion;

/// <summary>
/// Configuración de la conciliación (plan §2.2 y §1.5), sección <see cref="Seccion"/> (<c>Impuestos__Conciliacion__*</c>). Los
/// valores por defecto son los del plan: con webhook, primer GET a los 2 min, cada 5 min hasta 24 h y luego cada 6 h; sin webhook,
/// cada 10 s durante 10 min; «Incierto» con secreto, 30 min de espera y hasta 10 páginas de 50. Los TimeSpan van como
/// <c>hh:mm:ss</c>. Un valor no positivo impide arrancar (<see cref="Validar"/>).
/// </summary>
/// <example>
/// <code>
/// "Impuestos": { "Conciliacion": { "EsperaIncierto": "00:30:00", "TrabajadorActivo": true } }
/// </code>
/// </example>
public sealed class OpcionesConciliacion
{
    public const string Seccion = "Impuestos:Conciliacion";

    /// <summary>Corre el trabajador en segundo plano (las pruebas lo apagan y llaman la ronda a mano).</summary>
    public bool TrabajadorActivo { get; set; } = true;

    /// <summary>Pausa entre rondas del trabajador (menor que <see cref="IntervaloSinWebhook"/>).</summary>
    public TimeSpan IntervaloTrabajador { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Ítems por ronda en cada pasada (enviados e inciertos).</summary>
    public int LotePorRonda { get; set; } = 50;

    public TimeSpan PrimerGetConWebhook { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan IntervaloConWebhook { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Desde el envío, hasta cuándo rige <see cref="IntervaloConWebhook"/>; después, <see cref="IntervaloLargo"/>.</summary>
    public TimeSpan HastaIntervaloLargo { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan IntervaloLargo { get; set; } = TimeSpan.FromHours(6);

    public TimeSpan IntervaloSinWebhook { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Desde el envío, cuánto dura el ritmo de <see cref="IntervaloSinWebhook"/>.</summary>
    public TimeSpan DuracionSinWebhook { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Espera del «Incierto» con secreto antes de buscar su token en el listado del proveedor.</summary>
    public TimeSpan EsperaIncierto { get; set; } = TimeSpan.FromMinutes(30);

    public int PaginasIncierto { get; set; } = 10;

    public int LimitePaginaIncierto { get; set; } = 50;

    /// <summary>Si el listado falla, cuándo se vuelve a intentar la correlación.</summary>
    public TimeSpan ReintentoIncierto { get; set; } = TimeSpan.FromMinutes(5);

    public void Validar()
    {
        TimeSpan[] lapsos =
        [
            IntervaloTrabajador, PrimerGetConWebhook, IntervaloConWebhook, HastaIntervaloLargo, IntervaloLargo, IntervaloSinWebhook,
            DuracionSinWebhook, EsperaIncierto, ReintentoIncierto,
        ];
        if (lapsos.Any(l => l <= TimeSpan.Zero) || LotePorRonda < 1 || PaginasIncierto < 1 || LimitePaginaIncierto < 1)
            throw new InvalidOperationException($"{Seccion}: los intervalos deben ser positivos y los límites al menos 1.");
    }
}
