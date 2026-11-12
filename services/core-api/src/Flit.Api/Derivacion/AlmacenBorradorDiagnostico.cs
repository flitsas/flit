using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Api.Derivacion;

/// <summary>Trámite ya existente con una referencia externa de la empresa (lo que el rpc necesita, sin datos personales).</summary>
internal sealed record TramiteExistente(Guid Id, string Numero, string Estado, string? Origen, string ExternalRef);

/// <summary>Tipo de trámite publicado y activo, con su familia (para el permiso Crear de la familia).</summary>
internal sealed record TipoParaDerivar(string Codigo, string Familia);

/// <summary>
/// Resultado de crear el borrador con <see cref="CreateProcedureInstanceHandler"/>. <see cref="ReferenciaDuplicada"/>: otra
/// llamada ganó la carrera e insertó la misma <c>(tenant_id, external_ref)</c> (23505 de
/// <c>uq_procedure_instances_tenant_external_ref</c>).
/// </summary>
internal sealed record CreacionBorrador(ProcedureInstanceSummary? Resumen, string? Error, bool ReferenciaDuplicada = false);

/// <summary>Puerto de persistencia de <see cref="CreadorBorradorDiagnostico"/> (las pruebas lo reemplazan por un doble).</summary>
internal interface IAlmacenBorradorDiagnostico
{
    Task<TramiteExistente?> BuscarPorReferenciaAsync(Guid tenantId, string externalRef, CancellationToken ct);

    Task<IReadOnlyList<TramiteExistente>> BuscarPorReferenciasAsync(Guid tenantId, IReadOnlyCollection<string> externalRefs, CancellationToken ct);

    Task<TipoParaDerivar?> TipoPublicadoAsync(string codigo, CancellationToken ct);

    Task<CreacionBorrador> CrearAsync(CreateProcedureInstanceRequest request, CancellationToken ct);

    /// <summary>Campos editables (placa, VIN, documentos). Devuelve el código de error del caso de uso o null.</summary>
    Task<string?> SembrarCamposAsync(Guid id, Guid tenantId, Guid autor, IReadOnlyList<FieldValueInput> campos, bool soloFaltantes, CancellationToken ct);

    /// <summary>Siembra de sistema (organismo y ficha del vehículo). Devuelve el código de error del caso de uso o null.</summary>
    Task<string?> SembrarSistemaAsync(Guid id, Guid tenantId, IReadOnlyList<FieldValueInput> campos, bool soloFaltantes, CancellationToken ct);
}

/// <summary>
/// Adaptador sobre los casos de uso de Trámites que ya usan ICT y el portal (<see cref="CreateProcedureInstanceHandler"/>,
/// <see cref="PatchFieldValuesHandler"/>). No reimplementa reglas: solo lecturas acotadas a la empresa y la traducción de la
/// carrera del índice único. Un <c>ChangeTracker.Clear()</c> entre pasos, como <c>PersonaNaturalCreador</c>, para que un
/// guardado fallido no arrastre filas al siguiente.
/// </summary>
internal sealed class AlmacenBorradorDiagnostico(FlitDbContext db, CreateProcedureInstanceHandler crear, PatchFieldValuesHandler campos)
    : IAlmacenBorradorDiagnostico
{
    /// <summary>Índice único parcial de la idempotencia por referencia externa (<c>40-ICT-procedure-external-ref.sql</c>).</summary>
    internal const string IndiceReferenciaExterna = "uq_procedure_instances_tenant_external_ref";

    public async Task<TramiteExistente?> BuscarPorReferenciaAsync(Guid tenantId, string externalRef, CancellationToken ct) =>
        await db.Set<ProcedureInstance>().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.ExternalRef == externalRef && p.DeletedAt == null)
            .Select(p => new TramiteExistente(p.Id, p.ReferenceNumber, p.Status, p.Origin, p.ExternalRef!))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<TramiteExistente>> BuscarPorReferenciasAsync(Guid tenantId, IReadOnlyCollection<string> externalRefs, CancellationToken ct)
    {
        if (externalRefs.Count == 0)
            return [];
        var refs = externalRefs.ToList();
        return await db.Set<ProcedureInstance>().AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.ExternalRef != null && refs.Contains(p.ExternalRef) && p.DeletedAt == null)
            .Select(p => new TramiteExistente(p.Id, p.ReferenceNumber, p.Status, p.Origin, p.ExternalRef!))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<TipoParaDerivar?> TipoPublicadoAsync(string codigo, CancellationToken ct) =>
        await db.ProcedureTypes.AsNoTracking()
            .Where(t => t.Code == codigo && t.PublicationStatus == PublicationStatus.Published && t.IsActive)
            .Select(t => new TipoParaDerivar(t.Code, t.Family))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<CreacionBorrador> CrearAsync(CreateProcedureInstanceRequest request, CancellationToken ct)
    {
        try
        {
            var (resumen, error) = await crear.HandleAsync(request, ct).ConfigureAwait(false);
            return new CreacionBorrador(resumen, error);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndiceReferenciaExterna,
        })
        {
            // Otra llamada con la misma referencia ganó la carrera entre la búsqueda y el INSERT: el índice es la red final.
            db.ChangeTracker.Clear();
            return new CreacionBorrador(null, null, ReferenciaDuplicada: true);
        }
    }

    public async Task<string?> SembrarCamposAsync(Guid id, Guid tenantId, Guid autor, IReadOnlyList<FieldValueInput> items, bool soloFaltantes, CancellationToken ct)
    {
        var pendientes = await FiltrarAsync(id, items, soloFaltantes, ct).ConfigureAwait(false);
        if (pendientes.Count == 0)
            return null;
        db.ChangeTracker.Clear();
        var (_, error) = await campos.HandleAsync(id, tenantId, new PatchFieldValuesRequest(pendientes), autor, ct).ConfigureAwait(false);
        return error;
    }

    public async Task<string?> SembrarSistemaAsync(Guid id, Guid tenantId, IReadOnlyList<FieldValueInput> items, bool soloFaltantes, CancellationToken ct)
    {
        var pendientes = await FiltrarAsync(id, items, soloFaltantes, ct).ConfigureAwait(false);
        if (pendientes.Count == 0)
            return null;
        db.ChangeTracker.Clear();
        var (_, error) = await campos.HandleSystemSeedAsync(id, tenantId, new PatchFieldValuesRequest(pendientes), ct).ConfigureAwait(false);
        return error;
    }

    /// <summary>En un reintento solo se escribe lo que falta: lo existente pudo editarlo el gestor.</summary>
    private async Task<List<FieldValueInput>> FiltrarAsync(Guid id, IReadOnlyList<FieldValueInput> items, bool soloFaltantes, CancellationToken ct)
    {
        if (!soloFaltantes || items.Count == 0)
            return [.. items];
        var existentes = await db.Set<ProcedureInstanceFieldValue>().IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.ProcedureInstanceId == id)
            .Select(f => f.FieldKey)
            .ToListAsync(ct).ConfigureAwait(false);
        var ya = new HashSet<string>(existentes, StringComparer.OrdinalIgnoreCase);
        return [.. items.Where(i => !ya.Contains(i.FieldKey))];
    }
}
