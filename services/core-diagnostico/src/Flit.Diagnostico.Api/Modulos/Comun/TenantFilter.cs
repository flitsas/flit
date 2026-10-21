using System.Linq.Expressions;
using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Filtro global por empresa (HU #13722, ADR-0072, decisión B2 del plan §4): a toda entidad que implemente
/// <see cref="IEntidadDeEmpresa"/> se le aplica por convención un <c>HasQueryFilter</c> con la empresa del token
/// (<see cref="DiagnosticoDb.EmpresaActual"/>, de <c>IPlatformTenantAccessor</c>). Cerrado por defecto: sin empresa no se ve
/// ninguna fila y no se escribe ninguna. El SuperAdmin no se salta el filtro; la lectura entre empresas solo existe en
/// <c>/backoffice/*</c> con <c>IgnoreQueryFilters()</c> y auditoría (F00.1-H5, patrón ADR-0069).
/// </summary>
public static class TenantFilter
{
    /// <summary>Aplica el filtro a toda entidad de empresa del modelo. Se llama al final de <c>OnModelCreating</c>.</summary>
    public static void Aplicar(ModelBuilder modelBuilder, DiagnosticoDb db)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(db);

        // La constante es el contexto: EF la reemplaza en cada consulta por el contexto en curso, así que el filtro lee la
        // empresa de la petición actual aunque el modelo se construya una sola vez.
        var empresa = Expression.Property(Expression.Constant(db), nameof(DiagnosticoDb.EmpresaActual));
        foreach (var tipo in modelBuilder.Model.GetEntityTypes()
                     .Where(t => !t.IsOwned() && typeof(IEntidadDeEmpresa).IsAssignableFrom(t.ClrType))
                     .Select(t => t.ClrType).ToList())
        {
            var fila = Expression.Parameter(tipo, "fila");
            var tenantId = Expression.Convert(Expression.Property(fila, nameof(IEntidadDeEmpresa.TenantId)), typeof(Guid?));
            var cuerpo = Expression.AndAlso(
                Expression.NotEqual(empresa, Expression.Constant(null, typeof(Guid?))),
                Expression.Equal(tenantId, empresa));
            modelBuilder.Entity(tipo).HasQueryFilter(Expression.Lambda(cuerpo, fila));
        }
    }

    /// <summary>
    /// Antes de guardar: sin empresa en el token no se escribe ninguna entidad de empresa; una nueva sin <c>TenantId</c>
    /// toma la del token; cualquier fila (nueva, modificada o borrada) de otra empresa hace fallar todo el guardado.
    /// </summary>
    public static void ValidarEscritura(ChangeTracker cambios, Guid? empresa)
    {
        ArgumentNullException.ThrowIfNull(cambios);
        foreach (var entrada in cambios.Entries<IEntidadDeEmpresa>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var tipo = entrada.Metadata.ClrType.Name;
            if (empresa is not { } propia)
                throw new EscrituraEntreEmpresasException($"No hay empresa en el token: no se puede guardar {tipo}.");

            if (entrada.State == EntityState.Added && entrada.Entity.TenantId == Guid.Empty)
                entrada.Entity.TenantId = propia;

            var original = entrada.State == EntityState.Added
                ? entrada.Entity.TenantId
                : entrada.Property(e => e.TenantId).OriginalValue;
            var ajena = entrada.Entity.TenantId != propia ? entrada.Entity.TenantId : original;
            if (ajena != propia)
                throw new EscrituraEntreEmpresasException($"{tipo} de la empresa {ajena} no se puede guardar con el token de la empresa {propia}.");
        }
    }
}

/// <summary>Se intentó escribir una fila de otra empresa, o sin empresa en el token.</summary>
public sealed class EscrituraEntreEmpresasException : InvalidOperationException
{
    public EscrituraEntreEmpresasException()
    {
    }

    public EscrituraEntreEmpresasException(string message)
        : base(message)
    {
    }

    public EscrituraEntreEmpresasException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
