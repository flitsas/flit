using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
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
        foreach (var (entidad, tabla) in new[]
                 {
                     (typeof(ConfiguracionEmpresa), "configuracion_empresa"), (typeof(CambioContrato), "cambios_contrato"),
                     (typeof(CabeceraDiagnostico), "diagnosticos"), (typeof(HistorialEstadoDiagnostico), "historial_estado_diagnostico"),
                     (typeof(VehiculoDiagnostico), "vehiculos"), (typeof(CeldaConsulta), "consultas"), (typeof(IntentoConsulta), "intentos_consulta"),
                 })
        {
            db.Model.FindEntityType(entidad).Should().NotBeNull($"diagnostico.{tabla} está mapeada (HU #13736, #13738, #13735, #13723)");
            db.Model.FindEntityType(entidad)!.GetTableName().Should().Be(tabla);
        }
    }

    [Fact]
    public void Entidad_sin_IEntidadDeEmpresa_o_sin_filtro_se_detecta()
    {
        using var db = new ModeloConHuecos();

        SinEmpresaOSinFiltro(db.Model).Should().BeEquivalentTo(
            [$"{typeof(Huerfana).FullName} (no implementa IEntidadDeEmpresa)", $"{typeof(SinFiltro).FullName} (sin filtro por empresa)"]);
    }

    /// <summary>
    /// Entidades de negocio sin <see cref="IEntidadDeEmpresa"/> o sin filtro global. Quedan fuera las del SDK (outbox, inbox),
    /// las llaves de Data Protection (HU #13735), que son del servicio y no de una empresa, y la lista cerrada de
    /// <see cref="CatalogosGlobales"/> (excepción por tabla de ADR-0072).
    /// </summary>
    internal static IReadOnlyList<string> SinEmpresaOSinFiltro(IModel modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var sinEmpresa = new[]
        {
            typeof(Flit.Platform.Sdk.Messaging.OutboxMessage).Assembly,
            typeof(Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey).Assembly,
        };
        return modelo.GetEntityTypes()
            .Where(t => !t.IsOwned() && !sinEmpresa.Contains(t.ClrType.Assembly) && !CatalogosGlobales.Contains(t.ClrType))
            .Select(t => !typeof(IEntidadDeEmpresa).IsAssignableFrom(t.ClrType)
                ? $"{t.ClrType.FullName} (no implementa IEntidadDeEmpresa)"
                : t.GetDeclaredQueryFilters().Count == 0 ? $"{t.ClrType.FullName} (sin filtro por empresa)" : null)
            .OfType<string>().ToList();
    }

    /// <summary>
    /// Catálogos globales de FLIT sin <c>tenant_id</c> (plan §5, HU #13723, excepción por tabla de ADR-0072): la réplica de
    /// organismos y la caché de departamentos con impuesto. Lista cerrada: sumar uno exige su excepción en el ADR.
    /// </summary>
    internal static readonly IReadOnlySet<Type> CatalogosGlobales = new HashSet<Type> { typeof(Organismo), typeof(DepartamentoImpuesto) };

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
