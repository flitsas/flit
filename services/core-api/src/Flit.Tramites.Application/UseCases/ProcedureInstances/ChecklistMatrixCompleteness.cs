using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Construye los ítems base del checklist desde la matriz documental del gestor (HU #10522,
/// RF17/RF22). El nombre visible es el del tipo en Documental (<c>document_types.name</c>);
/// el id/ayuda del catálogo en código se conservan para reglas condicionales. El gestor manda
/// <b>presencia, obligatoriedad y orden</b> (la matriz ya viene ordenada por el resolutor).
/// </summary>
public static class MatrixChecklistItems
{
    public static IReadOnlyList<ChecklistItem> Build(string? codigo, IReadOnlyList<ResolvedChecklistDoc> matriz)
    {
        ArgumentNullException.ThrowIfNull(matriz);

        var known = (TramiteTipologiaCatalog.Get(codigo)?.Checklist ?? [])
            .Where(i => !string.IsNullOrEmpty(i.DocTipo))
            .GroupBy(i => i.DocTipo!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var items = new List<ChecklistItem>(matriz.Count);
        foreach (var m in matriz)
        {
            items.Add(known.TryGetValue(m.Codigo, out var cat)
                ? cat with { Obligatorio = m.Obligatorio, Label = m.Nombre }
                : new ChecklistItem(m.Codigo, m.Nombre, m.Obligatorio, m.Codigo));
        }

        return items;
    }
}

/// <summary>
/// Servicio de completitud documental "el gestor manda" (HU #10522, RF17/RF22 — matriz viva).
/// Centraliza el cómputo del checklist desde la matriz del gestor para que <b>display y gates</b>
/// (radicación <see cref="SubmitGate"/>, finalizar borrador <see cref="FinalizeDraftGate"/>,
/// consolidado y estado del wizard) coincidan.
/// <para>
/// Devuelve <c>null</c> cuando el proveedor no está inyectado (tests): el llamador usa el
/// gate del catálogo. Matriz vacía = sin documentos en Documental ⇒ checklist vacío y
/// completo (nada que cargar). Con matriz presente, computa desde la matriz + reglas
/// condicionales (RF30) + parámetros por gestora (RF31).
/// </para>
/// </summary>
public sealed class ChecklistMatrixCompleteness(
    IChecklistCompanyParamsProvider companyParams,
    IResolvedChecklistMatrixProvider? matrixProvider = null,
    IDocumentTypeCatalog? documentTypes = null)
{
    /// <summary>Resultado completo del checklist "gestor manda", o <c>null</c> si no aplica.</summary>
    public async Task<ChecklistResultado?> TryComputeAsync(
        ProcedureInstance instance,
        Guid tenantId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (matrixProvider is null)
            return null;

        var matriz = await matrixProvider
            .GetForAsync(instance.ProcedureTypeId, instance.TransitOfficeId, ct)
            .ConfigureAwait(false);
        if (matriz.Count == 0)
        {
            // Documental no asoció documentos a este tipo: nada que cargar, nada que bloquear.
            return ChecklistResultado.Vacio(instance.TypeCode);
        }

        var carga = matriz.Where(d => !d.EsGeneradoSistema).ToList();
        if (carga.Count == 0 && matriz.All(d => d.EsGeneradoSistema))
        {
            // Solo hay documentos generados: el gestor no tiene nada que cargar ⇒ completo.
            return ChecklistResultado.Vacio(instance.TypeCode);
        }

        var manual = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        var docTipos = instance.Attachments.Select(a => a.Tipo).ToList();
        var codigo = instance.TypeCode;
        var context = TramiteDocumentContextMapper.From(instance);
        var rules = ConditionalDocumentRules.For(codigo);
        var parametros = await companyParams.GetForTenantAsync(tenantId, ct).ConfigureAwait(false);
        var baseItems = MatrixChecklistItems.Build(codigo, carga);

        var computed = ChecklistEngine.ComputeFromMatrix(
            codigo, baseItems, manual, docTipos, context, rules, parametros);
        if (documentTypes is not null)
        {
            var generated = await documentTypes.ListSystemGeneratedCodesAsync(ct).ConfigureAwait(false);
            computed = ChecklistEngine.ExcludeFromGestorCarga(computed, generated);
        }
        else
        {
            var fromMatrix = new HashSet<string>(
                matriz.Where(d => d.EsGeneradoSistema).Select(d => d.Codigo),
                StringComparer.OrdinalIgnoreCase);
            computed = ChecklistEngine.ExcludeFromGestorCarga(computed, fromMatrix);
        }

        return computed;
    }

    /// <summary>
    /// Solo la completitud de obligatorios "gestor manda" para los gates, o <c>null</c> si no aplica.
    /// </summary>
    public async Task<bool?> TryComputeCompletoAsync(
        ProcedureInstance instance,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var computed = await TryComputeAsync(instance, tenantId, ct).ConfigureAwait(false);
        return computed?.Completo;
    }
}
