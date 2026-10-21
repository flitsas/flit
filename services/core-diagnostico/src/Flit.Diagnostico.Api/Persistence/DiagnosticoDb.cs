using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Platform.Sdk.Messaging;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Diagnostico.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>diagnostico</c>), del que es dueño su usuario de base (HU #13330). Nace con la
/// outbox y la bandeja de entrada del SDK; las tablas propias se agregan con su <c>IEntityTypeConfiguration</c> en
/// <c>Configuraciones/</c> y su migración. Toda entidad de negocio implementa <see cref="IEntidadDeEmpresa"/>: el filtro por
/// la empresa del token y la validación al guardar los pone <see cref="TenantFilter"/> (HU #13722, ADR-0072).
/// </summary>
public sealed class DiagnosticoDb(DbContextOptions<DiagnosticoDb> options, IPlatformTenantAccessor empresa) : DbContext(options)
{
    public const string Schema = ServicioSettings.Codigo;

    /// <summary>Empresa del token de la petición en curso; null si no hay (entonces el filtro no deja ver nada).</summary>
    public Guid? EmpresaActual => empresa.Current.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DiagnosticoDb).Assembly);
        TenantFilter.Aplicar(modelBuilder, this);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TenantFilter.ValidarEscritura(ChangeTracker, EmpresaActual);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TenantFilter.ValidarEscritura(ChangeTracker, EmpresaActual);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
            npgsql.EnableRetryOnFailure(3);
        }).UseSnakeCaseNamingConvention();
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class DiagnosticoDbDesignFactory : IDesignTimeDbContextFactory<DiagnosticoDb>
{
    public DiagnosticoDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DiagnosticoDb>();
        DiagnosticoDb.Configure(options, "Host=localhost;Database=diseno");
        return new DiagnosticoDb(options.Options, new SinEmpresa());
    }

    private sealed class SinEmpresa : IPlatformTenantAccessor
    {
        public PlatformTenantContext Current => PlatformTenantContext.None;
    }
}
