using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Quipux;

/// <summary>
/// Persiste la bitácora de una radicación en <c>tramites.quipux_submission_events</c>. Es la
/// trazabilidad "de la ejecución a la finalización" que exige la HU: cada etapa del ciclo
/// (claim → consolidado → S3 → registro → consulta → desenlace) deja su fila.
///
/// PORQUÉ SCOPE PROPIO: usa un scope y <see cref="FlitDbContext"/> PROPIOS (vía
/// <see cref="IServiceScopeFactory"/>, nunca el DbContext del llamador) y hace <c>SaveChanges</c>
/// inmediato, de modo que el evento queda registrado aunque el caso de uso llamador haga rollback
/// o reviente. Es exactamente el patrón de <c>IdentityValidationAuditLog</c> y
/// <c>AuditFailureWriter</c>: si la traza compartiera la unidad de trabajo del negocio, el rastro
/// de un fallo desaparecería junto con el fallo — justo el rastro que más importa.
///
/// PORQUÉ TRAGA EXCEPCIONES: auditar NUNCA debe romper el negocio. Un fallo al escribir la
/// bitácora se degrada a Warning y el worker sigue. Corolario incómodo: como el fallo es
/// silencioso, todo error previsible (vocabulario inválido) se valida ANTES de tocar la BD — ver
/// más abajo.
/// </summary>
/// <remarks>
/// <para>
/// PII — REGLA MÁS FUERTE DEL REPO: <c>detail</c> es responsabilidad del LLAMADOR y debe ser un
/// payload YA SANITIZADO. Pasa solo campos seguros: códigos de Quipux, descripciones de estado,
/// nombre del documento, duraciones, conteos, número de intento. NUNCA el request/response crudo,
/// NUNCA el actor ni su documento de identidad, NUNCA credenciales ni tokens. FLIT 1.0 persistía
/// <c>json_send</c>/<c>json_response</c> completos y con ello el documento y el nombre del
/// propietario acabaron en una tabla de logs: ese es el defecto que esta clase existe para no
/// repetir. Esta clase no puede sanitizar por ti — no conoce la forma del objeto que recibe.
/// </para>
/// <para>
/// Para recortar strings de diagnóstico largos (p. ej. el cuerpo de error del proveedor) usa
/// <see cref="Truncar"/> antes de componer el <c>detail</c>, igual que <c>SafeReadBodyAsync</c>
/// en <c>KyverumVerifyClient</c>.
/// </para>
/// </remarks>
internal sealed class QuipuxSubmissionAuditLog(
    IServiceScopeFactory scopeFactory,
    ILogger<QuipuxSubmissionAuditLog> logger) : IQuipuxAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Tope del detalle escalar. Mismo criterio que la bitácora de identidad (1000).</summary>
    private const int MaxDetailLength = 1000;

    /// <summary>Longitud por defecto de <see cref="Truncar"/>: la de <c>SafeReadBodyAsync</c>.</summary>
    private const int DefaultTruncateLength = 500;

    /// <summary>
    /// Vocabulario cerrado de <c>stage</c>. Espeja el CHECK del DDL: validar aquí convierte un
    /// INSERT que explotaría (y cuya excepción esta clase se tragaría en silencio) en un Warning
    /// visible que apunta al valor culpable.
    /// </summary>
    private static readonly HashSet<string> StagesValidos = new(StringComparer.Ordinal)
    {
        QuipuxStage.Claimed,
        QuipuxStage.ConsolidadoGenerado,
        QuipuxStage.S3Subido,
        QuipuxStage.RegistroEnviado,
        QuipuxStage.RegistroRespuesta,
        QuipuxStage.RegistroError,
        QuipuxStage.ConsultaEnviada,
        QuipuxStage.ConsultaRespuesta,
        QuipuxStage.ConsultaError,
        QuipuxStage.Aprobado,
        QuipuxStage.Rechazado,
        QuipuxStage.DeadLetter,
        QuipuxStage.ReintentoManual,
        QuipuxStage.CanceladoManual,
    };

    /// <summary>Vocabulario cerrado de <c>outcome</c>. Espeja el CHECK del DDL.</summary>
    private static readonly HashSet<string> OutcomesValidos = new(StringComparer.Ordinal)
    {
        QuipuxOutcome.Ok,
        QuipuxOutcome.ErrorTransitorio,
        QuipuxOutcome.ErrorDefinitivo,
        QuipuxOutcome.Omitido,
    };

    /// <inheritdoc />
    public async Task WriteAsync(
        Guid tenantId,
        Guid submissionId,
        string stage,
        string outcome,
        object? detail = null,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        // Validación ANTES de la BD: el DDL tiene CHECK sobre stage/outcome. Un valor fuera del
        // vocabulario haría fallar el INSERT, y como el catch de abajo traga todo, el evento se
        // perdería sin dejar rastro. Preferimos un Warning que nombre el valor inválido.
        if (!StagesValidos.Contains(stage))
        {
            QuipuxAuditLogMessages.StageInvalido(logger, stage, submissionId);
            return;
        }

        if (!OutcomesValidos.Contains(outcome))
        {
            QuipuxAuditLogMessages.OutcomeInvalido(logger, outcome, stage, submissionId);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();

            var row = new QuipuxSubmissionEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SubmissionId = submissionId,
                Stage = stage,
                Outcome = outcome,
                Detail = ToJsonDetail(detail),
                CorrelationId = correlationId,
                OccurredAt = DateTimeOffset.UtcNow,
            };

            if (!db.Database.IsRelational())
            {
                // Tests (InMemory): sin transacciones ni RLS — inserción directa.
                db.QuipuxSubmissionEvents.Add(row);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            // El DbContext usa EnableRetryOnFailure ⇒ la transacción manual va dentro de la
            // execution strategy (reintenta el bloque completo ante fallos transitorios de BD).
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear(); // estado limpio en cada (re)intento de la strategy
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

                // RLS: tramites.quipux_submission_events tiene la política tenant_isolation sobre
                // el GUC app.current_tenant_id, y el escritor lo invoca un BackgroundService
                // cross-tenant que no tiene contexto de request. Se fija el GUC al tenant de la
                // FILA que se escribe (is_local := true ⇒ vive solo dentro de esta transacción),
                // parametrizado y sin concatenar SQL. Es el enfoque de AuditFailureWriter y de los
                // repositorios del repo, y es el de mínimo privilegio: NO se desactiva RLS con
                // SET LOCAL row_security = off (eso queda para las lecturas cross-tenant de
                // SuperAdmin, p. ej. OtOperabilityGate), porque aquí siempre conocemos el tenant
                // exacto y así el motor sigue verificando que la fila cae donde debe.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                    cancellationToken);

                db.QuipuxSubmissionEvents.Add(row);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            });
        }
        catch (Exception ex)
        {
            // La auditoría nunca rompe el negocio: se degrada a Warning y el worker continúa.
            QuipuxAuditLogMessages.EscrituraFallida(logger, stage, outcome, submissionId, ex);
        }
    }

    /// <summary>
    /// Recorta un string de diagnóstico para que no infle la bitácora (cuerpos de error de
    /// Quipux, mensajes de excepción). Mismo criterio que <c>SafeReadBodyAsync</c> de
    /// <c>KyverumVerifyClient</c>. Úsalo al COMPONER el <c>detail</c>: truncar no es sanitizar —
    /// un cuerpo crudo recortado sigue siendo un cuerpo crudo y puede llevar PII dentro.
    /// </summary>
    internal static string? Truncar(string? value, int max = DefaultTruncateLength) =>
        value is not null && value.Length > max ? value[..max] : value;

    /// <summary>
    /// La columna <c>detail</c> es <c>jsonb</c>. Un string plano de diagnóstico (p. ej.
    /// <c>intento=2/5</c>) NO es JSON válido y rompería el INSERT con 22P02, así que se envuelve
    /// como escalar JSON (string entrecomillado) y se trunca antes. Cualquier otro objeto se
    /// serializa como documento JSON con las convenciones web del repo. Copiado de
    /// <c>IdentityValidationAuditLog.ToJsonDetail</c>.
    /// </summary>
    private static string? ToJsonDetail(object? detail) => detail switch
    {
        null => null,
        string s => JsonSerializer.Serialize(Truncar(s, MaxDetailLength), JsonOptions),
        _ => JsonSerializer.Serialize(detail, JsonOptions),
    };
}

/// <summary>Logging source-generated (CA1848) de la bitácora de radicaciones Quipux.</summary>
internal static partial class QuipuxAuditLogMessages
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Etapa Quipux fuera del vocabulario (stage={Stage}, submission={SubmissionId}); evento descartado sin escribir.")]
    public static partial void StageInvalido(ILogger logger, string stage, Guid submissionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Resultado Quipux fuera del vocabulario (outcome={Outcome}, stage={Stage}, submission={SubmissionId}); evento descartado sin escribir.")]
    public static partial void OutcomeInvalido(ILogger logger, string outcome, string stage, Guid submissionId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No se pudo escribir la bitácora Quipux (stage={Stage}, outcome={Outcome}, submission={SubmissionId}); no bloquea el flujo.")]
    public static partial void EscrituraFallida(ILogger logger, string stage, string outcome, Guid submissionId, Exception ex);
}
