namespace Flit.Modules.Quipux.Domain.LogQx;

/// <summary>
/// Lectura de la bandeja del LOG QX (HU #11786): lista los trámites con integración Quipux, UNO POR
/// FILA, con filtros combinables y contadores por estado.
/// </summary>
/// <remarks>
/// <para>Solo consulta, como <see cref="IQuipuxLogRepository"/>: sin claim, sin transiciones y
/// cross-tenant (el rol de core-api es propietario de las tablas y no le aplica su RLS
/// <c>tenant_isolation</c>). El acotado lo dan los filtros explícitos, no el GUC.</para>
/// <para>El universo NO es <c>quipux_submissions</c>: son los trámites cuyo TIPO declara integración
/// Quipux, tengan radicación o no. Los que no la tienen y además son elegibles aparecen como
/// <c>sin_radicar</c> — precisamente el caso que hoy no se puede diagnosticar.</para>
/// </remarks>
public interface IQuipuxBandejaRepository
{
    Task<QuipuxBandejaPage> SearchAsync(
        QuipuxBandejaQuery query,
        CancellationToken cancellationToken = default);
}
