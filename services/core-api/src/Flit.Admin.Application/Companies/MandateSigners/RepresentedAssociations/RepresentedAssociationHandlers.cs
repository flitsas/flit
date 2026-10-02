using System.Text;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.MandateSigners.PhysicalSignatureMigration;
using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Admin.Application.Companies.MandateSigners.RepresentedAssociations;

/// <summary>
/// HU #13176 — reporte de mandatarios impactados por el retiro de las asociaciones por Representante Legal,
/// ordenado por compañía, organismo y mandatario. Solo lectura.
/// </summary>
public sealed class GetRepresentedAssociationImpactReportHandler
{
    private readonly IRepresentedAssociationRetirementStore _store;

    public GetRepresentedAssociationImpactReportHandler(IRepresentedAssociationRetirementStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<RepresentedAssociationImpactRow>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _store.ListImpactedAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. rows
                .OrderBy(r => r.CompanyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.TransitOfficeName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.MandateSignerName, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}

/// <summary>Resultado del retiro (AC2/AC5).</summary>
public sealed record RetireRepresentedAssociationsResult(int RetiredRows, DateTimeOffset ExecutedAt);

/// <summary>
/// HU #13176 — retira las asociaciones por Representante Legal SOLO si se confirmó el aviso previo a los
/// clientes (AC3: sin confirmación → <see cref="RepresentedAssociationNoticeNotConfirmedException"/> y ninguna
/// fila cambia). Deja constancia de auditoría con cantidad, fecha y rol, sin nombres personales (AC2). Repetirlo
/// no falla: devuelve 0 filas (AC5). La confirmación es un dato del request, no un estado guardado.
/// </summary>
public sealed class RetireRepresentedAssociationsHandler
{
    private readonly IRepresentedAssociationRetirementStore _store;
    private readonly IAdminAuditWriter _audit;
    private readonly TimeProvider _time;

    public RetireRepresentedAssociationsHandler(
        IRepresentedAssociationRetirementStore store,
        IAdminAuditWriter audit,
        TimeProvider? time = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? TimeProvider.System;
    }

    public async Task<RetireRepresentedAssociationsResult> HandleAsync(
        bool confirmaAvisoEnviado,
        Guid? actorUserId,
        string actorRole,
        CancellationToken cancellationToken = default)
    {
        if (!confirmaAvisoEnviado)
        {
            throw new RepresentedAssociationNoticeNotConfirmedException();
        }

        var retired = await _store.RetireActiveAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();

        await _audit.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Companies,
                EntityName: "mandate_signer_represented_companies",
                Operation: AuditVocabulary.Operations.Delete,
                Result: AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: actorUserId,
                TargetEntityType: "MANDATE_SIGNER_REPRESENTED_COMPANIES",
                TargetEntityId: null,
                ClientIp: null,
                UserAgent: null,
                // Solo conteo, fecha y rol: nada de nombres, documentos ni NIT.
                NewValue: string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{{\"filasRetiradas\":{retired},\"fecha\":\"{now:O}\",\"rol\":\"{EscapeJson(actorRole)}\"}}")),
            cancellationToken).ConfigureAwait(false);

        return new RetireRepresentedAssociationsResult(retired, now);
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

/// <summary>Exportación CSV del reporte (UTF-8 con BOM para Excel; sin documento ni ruta de firma).</summary>
public static class RepresentedAssociationImpactCsv
{
    public const string FileName = "mandatarios-asociaciones-representante-legal.csv";

    private static readonly string[] Headers =
    [
        "compania_id", "compania", "organismo_id", "organismo", "mandatario_id", "mandatario",
        "cantidad_empresas_asociadas", "nit_empresas_asociadas",
    ];

    public static string Build(IReadOnlyList<RepresentedAssociationImpactRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.Append(string.Join(',', Headers)).Append("\r\n");
        foreach (var r in rows)
        {
            sb.Append(string.Join(',',
                    Cell(r.CompanyTenantId.ToString()),
                    Cell(r.CompanyName),
                    Cell(r.TransitOfficeId.ToString()),
                    Cell(r.TransitOfficeName),
                    Cell(r.MandateSignerId.ToString()),
                    Cell(r.MandateSignerName),
                    Cell(r.AssociatedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Cell(string.Join("; ", r.AssociatedNits))))
                .Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Escapa comillas y neutraliza fórmulas (= + - @) que Excel ejecutaría al abrir el archivo.</summary>
    public static string Cell(string? value) => PhysicalSignatureMigrationCsv.Cell(value);
}
