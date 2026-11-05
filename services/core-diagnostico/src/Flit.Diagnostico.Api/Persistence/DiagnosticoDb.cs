using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Platform.Sdk.Messaging;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Diagnostico.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>diagnostico</c>), del que es dueño su usuario de base (HU #13330). Nace con la
/// outbox y la bandeja de entrada del SDK; las tablas propias se agregan con su <c>IEntityTypeConfiguration</c> en
/// <c>Configuraciones/</c> y su migración. Toda entidad de negocio implementa <see cref="IEntidadDeEmpresa"/>: el filtro por
/// la empresa del token y la validación al guardar los pone <see cref="TenantFilter"/> (HU #13722, ADR-0072). También guarda las
/// llaves de Data Protection del servicio (<see cref="IDataProtectionKeyContext"/>, HU #13735).
/// </summary>
public sealed class DiagnosticoDb(DbContextOptions<DiagnosticoDb> options, IPlatformTenantAccessor empresa) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = ServicioSettings.Codigo;

    /// <summary>Llaves de Data Protection del servicio (<c>diagnostico.data_protection_keys</c>); sin empresa.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Empresa del token de la petición en curso; null si no hay (entonces el filtro no deja ver nada).</summary>
    public Guid? EmpresaActual => empresa.Current.TenantId;

    /// <summary>Ámbito abierto de escritura sobre otra empresa (HU #13741); null fuera de él.</summary>
    internal EscrituraAutorizada? EscrituraAutorizada { get; private set; }

    /// <summary>
    /// Abre, hasta que se libere el resultado, la escritura de filas de <paramref name="empresa"/> de los tipos dados (HU #13741,
    /// ADR-0073). Solo la llama el módulo Backoffice: <c>EscrituraEntreEmpresasSoloEnBackofficeTests</c> falla si aparece en
    /// otro lado. No se anida y no admite la empresa cero, una lista vacía ni un tipo que no sea de empresa.
    /// </summary>
    internal IDisposable AutorizarEscrituraDe(Guid empresa, params Type[] tipos)
    {
        ArgumentNullException.ThrowIfNull(tipos);
        if (empresa == Guid.Empty)
            throw new ArgumentException("La escritura entre empresas exige una empresa.", nameof(empresa));
        if (tipos.Length == 0 || tipos.Any(t => !typeof(IEntidadDeEmpresa).IsAssignableFrom(t)))
            throw new ArgumentException("La escritura entre empresas exige uno o más tipos de entidad de empresa.", nameof(tipos));
        if (EscrituraAutorizada is not null)
            throw new InvalidOperationException("Ya hay una escritura entre empresas abierta en este contexto.");

        EscrituraAutorizada = new EscrituraAutorizada(empresa, tipos.ToHashSet());
        return new Cierre(this);
    }

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
        TenantFilter.ValidarEscritura(ChangeTracker, EmpresaActual, EscrituraAutorizada);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TenantFilter.ValidarEscritura(ChangeTracker, EmpresaActual, EscrituraAutorizada);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private sealed class Cierre(DiagnosticoDb db) : IDisposable
    {
        public void Dispose() => db.EscrituraAutorizada = null;
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
