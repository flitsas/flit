using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13195 (ADR-0066 D1) — reporte previo de solo lectura del colapso de vínculos mandatario-compañía.
/// Ejecuta el MISMO criterio que el DDL 128 (designado en la regla compañía×OT, luego firma válida, luego el
/// más reciente) pero solo con SELECT: no modifica nada y no devuelve documento, nombre ni correo (Ley 1581).
/// Mantener sincronizado con <c>128-HU13195-mandatario-uno-por-origen.sql</c> y el script de docs/sql.
/// </summary>
internal sealed class DbMandateSignerLinkCollapseReader : IMandateSignerLinkCollapseReader
{
    private const string Sql = """
        WITH links AS (
            SELECT c.id AS link_id, c.mandate_signer_id, c.transit_office_id, c.company_tenant_id, c.created_at,
                   CASE WHEN c.configured_by_scope = 'compania' THEN 'compania' ELSE 'organismo' END AS origin_group,
                   EXISTS (SELECT 1 FROM admin.company_ot_mandate_rules r
                            WHERE r.company_tenant_id = c.company_tenant_id
                              AND r.transit_office_id = c.transit_office_id
                              AND r.default_mandate_signer_id = c.mandate_signer_id) AS is_designated,
                   (s.is_active
                    AND s.deleted_at IS NULL
                    AND (s.validity_kind = 'fixed'
                         OR ((now() AT TIME ZONE 'America/Bogota')::date BETWEEN s.valid_from AND s.valid_to))
                    AND (s.signer_model <> 'natural'
                         OR (CASE
                                 WHEN COALESCE(s.signature_method,
                                               CASE WHEN s.signature_vault_id IS NOT NULL THEN 'baul' ELSE 'biometria' END) = 'baul'
                                 THEN EXISTS (SELECT 1 FROM admin.signature_vault v
                                               WHERE v.tenant_id = c.company_tenant_id
                                                 AND upper(btrim(v.document_type))   = upper(btrim(s.document_type))
                                                 AND upper(btrim(v.document_number)) = upper(btrim(s.document_number))
                                                 AND v.estado = 'activa'
                                                 AND (now() AT TIME ZONE 'America/Bogota')::date
                                                     BETWEEN v.vigencia_desde AND v.vigencia_hasta)
                                 ELSE EXISTS (SELECT 1 FROM tramites.procedure_instance_biometric_validations b
                                               WHERE b.tenant_id = c.company_tenant_id
                                                 AND b.status = 'aprobado'
                                                 AND b.deleted_at IS NULL
                                                 AND b.valid_until > now()
                                                 AND upper(btrim(b.document_type))   = upper(btrim(s.document_type))
                                                 AND upper(btrim(b.document_number)) = upper(btrim(s.document_number)))
                             END))) AS has_valid_signature
              FROM admin.mandate_signer_companies c
              JOIN admin.mandate_signers s ON s.id = c.mandate_signer_id
             WHERE c.is_active
        ),
        ranked AS (
            SELECT l.*,
                   count(*) OVER (PARTITION BY l.transit_office_id, l.company_tenant_id, l.origin_group) AS group_size,
                   row_number() OVER (PARTITION BY l.transit_office_id, l.company_tenant_id, l.origin_group
                                      ORDER BY l.is_designated DESC, l.has_valid_signature DESC, l.created_at DESC, l.link_id DESC) AS rn
              FROM links l
        )
        SELECT r.transit_office_id AS transit_office_id,
               COALESCE(o.code, '') AS transit_office_code,
               r.company_tenant_id AS company_tenant_id,
               r.origin_group AS origin_group,
               r.group_size::int AS group_size,
               r.link_id AS link_id,
               r.mandate_signer_id AS mandate_signer_id,
               CASE WHEN r.rn = 1 THEN 'conservar' ELSE 'inactivar' END AS action,
               CASE
                   WHEN k.is_designated THEN 'designado_en_regla'
                   WHEN k.has_valid_signature AND EXISTS (SELECT 1 FROM ranked x
                             WHERE x.transit_office_id = r.transit_office_id AND x.company_tenant_id = r.company_tenant_id
                               AND x.origin_group = r.origin_group AND NOT x.has_valid_signature) THEN 'firma_valida'
                   ELSE 'mas_reciente'
               END AS criterion
          FROM ranked r
          JOIN ranked k ON k.transit_office_id = r.transit_office_id AND k.company_tenant_id = r.company_tenant_id
                       AND k.origin_group = r.origin_group AND k.rn = 1
          LEFT JOIN catalogs.transit_offices o ON o.id = r.transit_office_id
         WHERE r.group_size > 1
        """;

    private readonly FlitDbContext _context;

    public DbMandateSignerLinkCollapseReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<MandateLinkCollapseRow>> ListAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _context.Database
            .SqlQueryRaw<CollapseRawRow>(Sql)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows
                .Where(r => transitOfficeId is null || r.TransitOfficeId == transitOfficeId)
                .Select(r => new MandateLinkCollapseRow(
                    r.TransitOfficeId, r.TransitOfficeCode, r.CompanyTenantId, r.OriginGroup, r.GroupSize,
                    r.LinkId, r.MandateSignerId, r.Action, r.Criterion)),
        ];
    }

    /// <summary>Forma de la consulta; <c>SqlQueryRaw</c> mapea por nombre de columna (snake_case).</summary>
    internal sealed class CollapseRawRow
    {
        public Guid TransitOfficeId { get; set; }
        public string TransitOfficeCode { get; set; } = string.Empty;
        public Guid CompanyTenantId { get; set; }
        public string OriginGroup { get; set; } = string.Empty;
        public int GroupSize { get; set; }
        public Guid LinkId { get; set; }
        public Guid MandateSignerId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Criterion { get; set; } = string.Empty;
    }
}
