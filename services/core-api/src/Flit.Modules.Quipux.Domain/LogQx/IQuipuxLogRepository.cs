namespace Flit.Modules.Quipux.Domain.LogQx;

/// <summary>
/// Lectura del LOG QX para la consola de soporte/administración (HU #10793). Solo consulta: busca
/// radicaciones por placa/trámite/radicado y devuelve cada una con su bitácora de eventos. No hace
/// claim ni transiciones —a diferencia del repositorio de los workers y del de la cola— y lee
/// cross-tenant (el rol de core-api es propietario de las tablas y no aplica su RLS
/// <c>tenant_isolation</c>, igual que <c>IQuipuxSubmissionConsoleRepository</c>).
/// </summary>
public interface IQuipuxLogRepository
{
    Task<QuipuxLogPage> SearchAsync(QuipuxLogQuery query, CancellationToken cancellationToken = default);
}
