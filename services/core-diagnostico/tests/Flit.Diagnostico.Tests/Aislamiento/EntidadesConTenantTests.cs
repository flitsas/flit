using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #13722 (F00.1-H2), escenario 4 — toda entidad mapeada en <see cref="DiagnosticoDb"/> que no sea del SDK (outbox,
/// inbox) implementa <see cref="IEntidadDeEmpresa"/> y lleva el filtro global por empresa. No necesita base: solo el modelo.
/// </summary>
public sealed class EntidadesConTenantTests
{
    [Fact]
    public void Toda_entidad_de_negocio_implementa_IEntidadDeEmpresa_y_tiene_filtro()
    {
        using var db = DosEmpresas.Contexto("Host=127.0.0.1;Database=solo_modelo", Guid.NewGuid());

        SinEmpresaOSinFiltro(db.Model).Should().BeEmpty("toda entidad de negocio va por empresa");
        db.Model.FindEntityType(typeof(LecturaBackoffice)).Should().NotBeNull("diagnostico.lecturas_backoffice está mapeada");
        db.Model.FindEntityType(typeof(LecturaBackoffice))!.GetTableName().Should().Be("lecturas_backoffice");
    }

    [Fact]
    public void Entidad_sin_IEntidadDeEmpresa_o_sin_filtro_se_detecta()
    {
        using var db = new ModeloConHuecos();

        SinEmpresaOSinFiltro(db.Model).Should().BeEquivalentTo(
            [$"{typeof(Huerfana).FullName} (no implementa IEntidadDeEmpresa)", $"{typeof(SinFiltro).FullName} (sin filtro por empresa)"]);
    }

    /// <summary>Entidades de negocio (todas salvo las del SDK) sin <see cref="IEntidadDeEmpresa"/> o sin filtro global.</summary>
    internal static IReadOnlyList<string> SinEmpresaOSinFiltro(IModel modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var sdk = typeof(Flit.Platform.Sdk.Messaging.OutboxMessage).Assembly;
        return modelo.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.ClrType.Assembly != sdk)
            .Select(t => !typeof(IEntidadDeEmpresa).IsAssignableFrom(t.ClrType)
                ? $"{t.ClrType.FullName} (no implementa IEntidadDeEmpresa)"
                : t.GetDeclaredQueryFilters().Count == 0 ? $"{t.ClrType.FullName} (sin filtro por empresa)" : null)
            .OfType<string>().ToList();
    }

    private sealed class Huerfana
    {
        public Guid Id { get; set; }
    }

    private sealed class SinFiltro : IEntidadDeEmpresa
    {
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
    }

    private sealed class ModeloConHuecos : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseNpgsql("Host=127.0.0.1;Database=solo_modelo");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Huerfana>();
            modelBuilder.Entity<SinFiltro>();
        }
    }
}
