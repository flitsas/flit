using Flit.Tramites.Domain.Integration;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Ítem de checklist computado, en la forma congelada que consume el frontend.
/// </summary>
public sealed record ChecklistItemDto(
    string Key,
    string Label,
    bool Obligatorio,
    string? DocTipo,
    bool Satisfied,
    // Límites de carga por tipo (RF08/09) para que el front pre-valide inline con el límite real.
    // null ⇒ el tipo no tiene regla propia ⇒ el front usa los defaults globales.
    long? MaxSizeBytes = null,
    IReadOnlyList<string>? MimeTypesAllowed = null);

public sealed record ChecklistResponse(
    IReadOnlyList<ChecklistItemDto> Items,
    IReadOnlyList<string> FaltanObligatorios,
    bool Completo);

/// <summary>
/// Computa el estado del checklist de una instancia: resuelve la tipología por
/// <c>tipologia_codigo</c> (o <c>modalidad_entrada</c>), combina el estado manual
/// (<c>checklist_estado</c>) con los tipos de documentos ya subidos (auto-marca) y delega el
/// cómputo en el dominio (<see cref="ChecklistEngine"/>). Sobre la lista base aplica además las
/// reglas condicionales por atributo del trámite (RF30/33/35/37/38/39, derivadas de los datos
/// persistidos por <see cref="TramiteDocumentContextMapper"/>) y los parámetros de la compañía
/// gestora (RF31). Sin datos que disparen condiciones ni parámetros configurados, el resultado
/// es idéntico al checklist plano ⇒ sin cambios de comportamiento.
/// <para>
/// HU #10522 (RF17/RF22 — decisión LT: <b>matriz viva</b>): cuando el gestor tiene una matriz
/// documental configurada para el trámite (<see cref="IResolvedChecklistMatrixProvider"/> devuelve
/// documentos), la lista, obligatoriedad y orden salen de la matriz — <b>el gestor manda</b>. Es el
/// comportamiento por defecto en todos los entornos (los seeds nivelan la matriz en DEV/QA/PDN). Si
/// un <c>procedure_type</c> aún no tiene matriz —o el proveedor no está inyectado (tests)— se cae al
/// catálogo plano (degradación natural, no una bandera).
/// </para>
/// </summary>
public sealed class GetChecklistHandler(
    IProcedureInstanceRepository repo,
    IChecklistCompanyParamsProvider companyParams,
    IResolvedChecklistMatrixProvider? matrixProvider = null,
    IDocumentTypeCatalog? documentTypes = null,
    IMandateRequirementPolicy? mandatePolicy = null)
{
    private const string TransitOfficeCodeFieldKey = "transit_office_code";
    private readonly IMandateRequirementPolicy _mandatePolicy = mandatePolicy ?? NullMandateRequirementPolicy.Instance;

    public async Task<(ChecklistResponse? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithChecklistGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();

        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);

        // RF30 — atributos del trámite derivados de los datos persistidos (actores, campos RUNT,
        // participantes) y sus reglas condicionales por tipología; RF31 — parámetros por gestora.
        // La supresión de cédula para persona natural (HU #10542) queda cubierta por la regla
        // condicional `pn_sin_cedula` (EsPersonaNatural ⇒ Hide cedulas), sin override aparte.
        // ADR-0036 (HU #10913) — configuración de mandato del OT por su código (field_values); null si
        // el OT no la tiene ⇒ mandato solo para persona jurídica (default).
        var otCode = instance.FieldValues?
            .FirstOrDefault(f => string.Equals(f.FieldKey, TransitOfficeCodeFieldKey, StringComparison.OrdinalIgnoreCase))
            ?.ValueText;
        var mandateConfig = string.IsNullOrWhiteSpace(otCode)
            ? null
            : await _mandatePolicy.ResolveAsync(otCode, ct);

        var context = TramiteDocumentContextMapper.From(instance, mandateConfig);
        var rules = ConditionalDocumentRules.For(codigo);
        var parametros = await companyParams.GetForTenantAsync(tenantId, ct);

        ChecklistResultado? computed = null;

        // RF17 + RF22 (matriz viva): si el gestor tiene documentos configurados para este trámite,
        // manda su lista, obligatoriedad y orden. Sin matriz ⇒ se cae al catálogo actual.
        if (matrixProvider is not null)
        {
            var matriz = await matrixProvider
                .GetForAsync(instance.ProcedureTypeId, instance.TransitOfficeId, ct);
            if (matriz.Count > 0)
            {
                var baseItems = MatrixChecklistItems.Build(codigo, matriz);
                computed = ChecklistEngine.ComputeFromMatrix(
                    codigo, baseItems, manual, docTipos, context, rules, parametros);
            }
        }

        // Fallback (sin matriz configurada): catálogo plano + condicionales ⇒ sin regresión.
        computed ??= ChecklistEngine.ComputeConditional(codigo, manual, docTipos, context, rules, parametros);
        if (computed is null)
            return (null, "tipologia_not_found");

        // Límites por-tipo (MIME/tamaño, RF08/09): el front los usa para pre-validar inline con el
        // límite real. Sin catálogo inyectado (tests) o tipo sin regla ⇒ límites null ⇒ default global.
        var limitsByTipo = new Dictionary<string, DocumentTypeRule>(StringComparer.OrdinalIgnoreCase);
        if (documentTypes is not null)
        {
            var tipos = computed.Items
                .Select(i => i.Item.DocTipo)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var tipo in tipos)
            {
                var rule = await documentTypes.GetRuleAsync(tipo, ct);
                if (rule is not null)
                    limitsByTipo[tipo] = rule;
            }
        }

        var items = computed.Items
            .Select(i =>
            {
                var rule = i.Item.DocTipo is not null && limitsByTipo.TryGetValue(i.Item.DocTipo, out var r)
                    ? r
                    : null;
                return new ChecklistItemDto(
                    i.Item.Id,
                    i.Item.Label,
                    i.Item.Obligatorio,
                    i.Item.DocTipo,
                    i.Satisfecho,
                    rule is { MaxSizeBytes: > 0 } ? rule.MaxSizeBytes : null,
                    rule is { MimeTypesAllowed.Count: > 0 } ? rule.MimeTypesAllowed : null);
            })
            .ToList();

        return (new ChecklistResponse(items, computed.FaltanObligatorios, computed.Completo), null);
    }
}
