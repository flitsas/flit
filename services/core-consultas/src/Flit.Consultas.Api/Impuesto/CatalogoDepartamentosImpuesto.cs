using Flit.Consultas.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>Un departamento del catálogo. <see cref="Activo"/> es la lista efectiva.</summary>
internal sealed record DepartamentoHabilitado(string Codigo, string Nombre, bool Activo);

/// <summary>Lectura de la lista efectiva. La escritura vive en <see cref="CatalogoDepartamentosImpuesto"/>.</summary>
internal interface ICatalogoDepartamentosImpuesto
{
    Task<IReadOnlyList<DepartamentoHabilitado>> ActivosAsync(CancellationToken ct);

    Task<bool> ActivoAsync(string? codigo, CancellationToken ct);
}

/// <summary>
/// Tabla <c>consultas.vehicle_tax_departments</c> (ADR-0080). Reemplaza <c>IMPUESTO_DEPARTAMENTOS</c>.
/// Un código fuera de la homologación no se guarda.
/// </summary>
internal sealed class CatalogoDepartamentosImpuesto(ConsultasDb db, TimeProvider time) : ICatalogoDepartamentosImpuesto
{
    public async Task<IReadOnlyList<DepartamentoHabilitado>> ActivosAsync(CancellationToken ct) =>
        await Filas().Where(d => d.IsActive && d.DeletedAt == null)
            .Select(d => new DepartamentoHabilitado(d.DepartmentCode, d.DepartmentName, true))
            .ToListAsync(ct).ConfigureAwait(false);

    public Task<bool> ActivoAsync(string? codigo, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(codigo)
            ? Task.FromResult(false)
            : db.DepartamentosImpuesto.AsNoTracking()
                .AnyAsync(d => d.DepartmentCode == codigo && d.IsActive && d.DeletedAt == null, ct);

    public async Task<IReadOnlyList<DepartamentoHabilitado>> ListarTodosAsync(CancellationToken ct) =>
        await Filas()
            .Select(d => new DepartamentoHabilitado(d.DepartmentCode, d.DepartmentName, d.IsActive && d.DeletedAt == null))
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<DepartamentoHabilitado> GuardarAsync(string codigo, string nombre, bool activo, CancellationToken ct)
    {
        var code = codigo.Trim();
        if (!HomologacionDocumentoImpuesto.Conocido(code))
            throw new InvalidOperationException("Código de departamento fuera de la homologación.");
        var nombreLimpio = nombre.Trim();
        if (nombreLimpio.Length is 0 or > 50)
            throw new InvalidOperationException("El nombre del departamento es obligatorio y de hasta 50 caracteres.");

        var fila = await db.DepartamentosImpuesto.SingleOrDefaultAsync(d => d.DepartmentCode == code, ct).ConfigureAwait(false);
        var ahora = time.GetUtcNow();
        if (fila is null)
        {
            fila = new DepartamentoConsultaImpuesto
            {
                Id = Guid.CreateVersion7(),
                DepartmentCode = code,
                CreatedAt = ahora,
            };
            db.DepartamentosImpuesto.Add(fila);
        }

        fila.DepartmentName = nombreLimpio;
        fila.IsActive = activo;
        fila.DeletedAt = activo ? null : ahora;
        fila.UpdatedAt = ahora;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new DepartamentoHabilitado(fila.DepartmentCode, fila.DepartmentName, fila.IsActive && fila.DeletedAt is null);
    }

    private IQueryable<DepartamentoConsultaImpuesto> Filas() =>
        db.DepartamentosImpuesto.AsNoTracking().OrderBy(d => d.DepartmentCode);
}
