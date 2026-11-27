using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Flit.Impuestos.Tests.Aislamiento;

/// <summary>
/// HU #14694 (M05, plan §4.1) — el modelo completo del esquema <c>impuestos</c> (las 13 tablas de §4.1) está mapeado y toda
/// entidad con <c>tenant_id</c> implementa <see cref="IEntidadDeEmpresa"/> y lleva el filtro global por empresa (forma de
/// ADR-0072). Quedan fuera las del SDK, las llaves de Data Protection y la lista cerrada de tablas globales. Con una empresa en
/// el contexto, una fila de otra empresa no se ve y no se puede guardar. No necesita base para el modelo.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-impuestos/tests/Flit.Impuestos.Tests --no-build --filter FullyQualifiedName~EntidadesConTenantTests
/// </code>
/// </example>
public sealed class EntidadesConTenantTests
{
    /// <summary>Tablas de §4.1 con <c>tenant_id</c> obligatorio: van por empresa.</summary>
    private static readonly (Type Entidad, string Tabla)[] DeEmpresa =
    [
        (typeof(AprovisionamientoEmpresa), "aprovisionamientos"),
        (typeof(CredencialEmpresa), "credenciales_empresa"),
        (typeof(HistorialAprovisionamiento), "historial_aprovisionamiento"),
        (typeof(ConfiguracionEmpresa), "configuracion_empresa"),
        (typeof(Liquidacion), "liquidaciones"),
        (typeof(LiquidacionItem), "liquidacion_items"),
        (typeof(LiquidacionPeriodo), "liquidacion_periodos"),
        (typeof(Recibo), "recibos"),
        (typeof(Notificacion), "notificaciones"),
        (typeof(LecturaBackoffice), "auditoria_lectura_backoffice"),
    ];

    /// <summary>Tablas globales de §4.1 (sin <c>tenant_id</c> o con uno opcional e informativo).</summary>
    private static readonly (Type Entidad, string Tabla)[] Globales =
    [
        (typeof(AvisoRecibido), "avisos_recibidos"),
        (typeof(Organismo), "organismos"),
        (typeof(FuenteApi), "fuentes_api"),
    ];

    [Fact]
    public void Las_13_tablas_de_4_1_estan_mapeadas_en_el_esquema_impuestos()
    {
        using var db = Contexto(Guid.NewGuid());

        foreach (var (entidad, tabla) in DeEmpresa.Concat(Globales))
        {
            var tipo = db.Model.FindEntityType(entidad);
            tipo.Should().NotBeNull($"impuestos.{tabla} está mapeada");
            tipo!.GetTableName().Should().Be(tabla);
            tipo!.GetSchema().Should().Be(ImpuestosDb.Schema);
        }

        DeEmpresa.Concat(Globales).Should().HaveCount(13);
    }

    [Fact]
    public void Toda_entidad_de_negocio_implementa_IEntidadDeEmpresa_y_tiene_filtro()
    {
        using var db = Contexto(Guid.NewGuid());

        SinEmpresaOSinFiltro(db.Model).Should().BeEmpty("toda entidad de negocio va por empresa");
        DeEmpresa.Should().AllSatisfy(e => typeof(IEntidadDeEmpresa).IsAssignableFrom(e.Entidad).Should().BeTrue(e.Tabla));
        ImpuestosDb.TablasGlobales.Should().BeEquivalentTo(Globales.Select(g => g.Entidad), "lista cerrada de tablas sin empresa");
    }

    [Fact]
    public void Entidad_sin_IEntidadDeEmpresa_o_sin_filtro_se_detecta()
    {
        using var db = new ModeloConHuecos();

        SinEmpresaOSinFiltro(db.Model).Should().BeEquivalentTo(
            [$"{typeof(Huerfana).FullName} (no implementa IEntidadDeEmpresa)", $"{typeof(SinFiltro).FullName} (sin filtro por empresa)"]);
    }

    [Fact]
    public void Sin_empresa_no_se_guarda_y_una_fila_de_otra_empresa_no_se_guarda()
    {
        var a = Guid.NewGuid();
        using var sinEmpresa = Contexto(null);
        using var deA = Contexto(a);

        sinEmpresa.Add(new Notificacion { LiquidacionId = Guid.NewGuid(), Tipo = "LOTE_TERMINADO", CreadaEn = DateTimeOffset.UtcNow });
        var sinToken = () => TenantFilter.ValidarEscritura(sinEmpresa.ChangeTracker, sinEmpresa.EmpresaActual);
        deA.Add(new Notificacion { TenantId = Guid.NewGuid(), LiquidacionId = Guid.NewGuid(), Tipo = "LOTE_TERMINADO", CreadaEn = DateTimeOffset.UtcNow });
        var ajena = () => TenantFilter.ValidarEscritura(deA.ChangeTracker, deA.EmpresaActual);

        sinToken.Should().Throw<EscrituraEntreEmpresasException>();
        ajena.Should().Throw<EscrituraEntreEmpresasException>();
    }

    [Fact]
    public void Una_fila_nueva_sin_empresa_toma_la_del_token()
    {
        var a = Guid.NewGuid();
        using var deA = Contexto(a);
        var notificacion = new Notificacion { LiquidacionId = Guid.NewGuid(), Tipo = "LOTE_TERMINADO", CreadaEn = DateTimeOffset.UtcNow };
        deA.Add(notificacion);

        TenantFilter.ValidarEscritura(deA.ChangeTracker, deA.EmpresaActual);

        notificacion.TenantId.Should().Be(a);
    }

    [Fact]
    public async Task Con_base_el_filtro_no_deja_ver_filas_de_otra_empresa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using (var deA = api.Contexto(a))
        {
            // R2: notificaciones.liquidacion_id tiene FK a liquidaciones.
            var lote = new Liquidacion { Tipo = TipoLiquidacion.Individual, TotalItems = 1, ConfigCopia = "{}", CreadaPor = Guid.NewGuid(), CreadaEn = DateTimeOffset.UtcNow };
            deA.Add(lote);
            deA.Add(new Notificacion { LiquidacionId = lote.Id, Tipo = "LOTE_TERMINADO", CreadaEn = DateTimeOffset.UtcNow });
            await deA.SaveChangesAsync(ct);
        }

        await using var deB = api.Contexto(b);
        await using var otraVezA = api.Contexto(a);
        await using var sinEmpresa = api.Contexto(null);

        (await deB.Set<Notificacion>().CountAsync(ct)).Should().Be(0, "B no ve lo de A");
        (await otraVezA.Set<Notificacion>().CountAsync(ct)).Should().Be(1);
        (await sinEmpresa.Set<Notificacion>().CountAsync(ct)).Should().Be(0, "sin empresa no se ve ninguna fila");
    }

    /// <summary>Entidades de negocio sin <see cref="IEntidadDeEmpresa"/> o sin filtro, fuera del SDK, Data Protection y las globales.</summary>
    internal static IReadOnlyList<string> SinEmpresaOSinFiltro(IModel modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        var sinEmpresa = new[]
        {
            typeof(Flit.Platform.Sdk.Messaging.OutboxMessage).Assembly,
            typeof(Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey).Assembly,
        };
        return modelo.GetEntityTypes()
            .Where(t => !t.IsOwned() && !sinEmpresa.Contains(t.ClrType.Assembly) && !ImpuestosDb.TablasGlobales.Contains(t.ClrType))
            .Select(t => !typeof(IEntidadDeEmpresa).IsAssignableFrom(t.ClrType)
                ? $"{t.ClrType.FullName} (no implementa IEntidadDeEmpresa)"
                : t.GetDeclaredQueryFilters().Count == 0 ? $"{t.ClrType.FullName} (sin filtro por empresa)" : null)
            .OfType<string>().ToList();
    }

    private static ImpuestosDb Contexto(Guid? empresa) =>
        new(new DbContextOptionsBuilder<ImpuestosDb>().UseNpgsql("Host=127.0.0.1;Database=solo_modelo").UseSnakeCaseNamingConvention().Options, new EmpresaFija(empresa));

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
