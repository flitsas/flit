using System.Text.Json;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Catalog;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Marca (o desmarca) el ítem de checklist de la impronta como "se generará automáticamente" en el
/// paso FUR, sin adjuntar el documento. Escribe el flag manual en <c>checklist_estado</c> (reusando
/// <see cref="ChecklistEstadoJson"/>), que <c>ChecklistEngine</c> honra como satisfecho por marca
/// (<c>porManual</c>) tanto en el catálogo plano como en la ruta "matriz manda". Así el gate de
/// completitud del paso 2 / finalizar borrador deja continuar aunque la impronta esté marcada
/// obligatoria por el gestor y aún no se haya cargado.
///
/// La radicación NO se debilita: <see cref="SubmitGate"/> exige el attachment REAL de impronta con
/// lógica independiente (<c>ImprontaGenerada</c>) que ignora este flag, así que diferir aquí nunca
/// permite radicar sin la impronta generada en el FUR.
///
/// Acotado EXCLUSIVAMENTE al tipo <c>impronta</c> — único documento con un gate de radicación propio
/// que exige el archivo real. NO se generaliza: un toggle manual sobre cualquier obligatorio dejaría
/// radicar sin él (p. ej. compraventa), abriendo un hueco.
/// </summary>
public sealed class SetImprontaDiferidaHandler(IProcedureInstanceRepository repo)
{
    public async Task<(bool? Ok, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        bool diferida,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithWizardGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");
        if (!TramiteEstado.PermiteEdicionDatos(instance.Status, instance.SubsanacionActiva))
            return (null, "not_draft");

        // Se resuelve el Id real del ítem (no se asume el literal "impronta"): en la ruta de matriz los
        // ítems conservan el id del catálogo, así que este mismo Id es la clave válida en ambos caminos.
        var codigo = TipologiaResolver.ResolveCodigo(instance.TipologiaCodigo, instance.ModalidadEntrada);
        var item = TramiteTipologiaCatalog.Get(codigo)?.Checklist
            .FirstOrDefault(i => string.Equals(i.DocTipo, "impronta", StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return (null, "impronta_no_aplica");

        var estado = ChecklistEstadoJson.Parse(instance.ChecklistEstado);
        bool changed;
        if (diferida)
        {
            changed = !(estado.TryGetValue(item.Id, out var v) && v);
            if (changed)
                estado[item.Id] = true;
        }
        else
        {
            // Quitar la marca. Si existe un adjunto real de impronta, el ítem sigue satisfecho por
            // documento (porDoc) — correcto: solo se limpia la intención de diferir.
            changed = estado.Remove(item.Id);
        }

        if (changed)
        {
            instance.ChecklistEstado = JsonSerializer.Serialize(estado);
            await repo.SaveChangesAsync(ct);
        }

        return (true, null);
    }
}
