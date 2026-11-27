using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Platform.Sdk.Messaging;
using Flit.Platform.Sdk.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>impuestos</c>), del que es dueño su usuario de base (<c>flit_impuestos</c>). Nace con la
/// outbox y la bandeja de entrada del SDK y suma el modelo completo del plan §4.1 en la migración <c>R0_Modelo</c> (HU #14694,
/// M05), con su <c>IEntityTypeConfiguration</c> por tabla en <c>Configuraciones/</c>. Toda entidad con <c>tenant_id</c>
/// implementa <see cref="IEntidadDeEmpresa"/>: el filtro por la empresa del token y la validación al guardar los pone
/// <see cref="TenantFilter"/>. Las tablas globales son la lista cerrada <see cref="TablasGlobales"/>. También guarda las llaves de
/// Data Protection del servicio (<see cref="IDataProtectionKeyContext"/>, plan §4.2).
/// </summary>
public sealed class ImpuestosDb(DbContextOptions<ImpuestosDb> options, IPlatformTenantAccessor empresa) : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = ServicioSettings.Codigo;

    /// <summary>
    /// Tablas de §4.1 sin empresa: el dedupe de avisos (el token puede no ser de nadie), la réplica de organismos y la caché de
    /// <c>GET /fuentes</c>. Lista cerrada: sumar una exige su razón en el plan.
    /// </summary>
    public static IReadOnlySet<Type> TablasGlobales { get; } = new HashSet<Type> { typeof(AvisoRecibido), typeof(Organismo), typeof(FuenteApi) };

    /// <summary>Llaves de Data Protection del servicio (<c>impuestos.data_protection_keys</c>); sin empresa.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Empresa del token de la petición en curso; null si no hay (entonces el filtro no deja ver nada).</summary>
    public Guid? EmpresaActual => empresa.Current.TenantId;

    /// <summary>Ámbito abierto de escritura sobre otra empresa; null fuera de él.</summary>
    internal EscrituraAutorizada? EscrituraAutorizada { get; private set; }

    /// <summary>
    /// Abre, hasta que se libere el resultado, la escritura de filas de <paramref name="empresa"/> de los tipos dados (backoffice
    /// del Super Admin). No se anida y no admite la empresa cero, una lista vacía ni un tipo que no sea de empresa.
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ImpuestosDb).Assembly);
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

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema y nombres en snake_case.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
            npgsql.EnableRetryOnFailure(3);
        }).UseSnakeCaseNamingConvention();

    private sealed class Cierre(ImpuestosDb db) : IDisposable
    {
        public void Dispose() => db.EscrituraAutorizada = null;
    }
}

/// <summary>
/// Un enum se guarda con su código en mayúsculas y guion bajo (<c>ListoSinWebhook</c> → <c>LISTO_SIN_WEBHOOK</c>), que es lo que
/// validan los CHECK de la base.
/// </summary>
internal static class CodigosDeEnum
{
    public static string Codigo<T>(T valor)
        where T : struct, Enum => JsonNamingPolicy.SnakeCaseUpper.ConvertName(valor.ToString());

    public static ValueConverter<T, string> Convertidor<T>()
        where T : struct, Enum => new(v => Codigo(v), c => Leer<T>(c));

    /// <summary>Valores permitidos de una columna de código, para su CHECK (<c>'A', 'B'</c>).</summary>
    public static string ListaSql<T>()
        where T : struct, Enum => string.Join(", ", Enum.GetValues<T>().Select(v => $"'{Codigo(v)}'"));

    public static T Leer<T>(string codigo)
        where T : struct, Enum
    {
        foreach (var v in Enum.GetValues<T>())
        {
            if (string.Equals(Codigo(v), codigo, StringComparison.Ordinal))
                return v;
        }

        throw new InvalidOperationException($"Código desconocido para {typeof(T).Name}.");
    }
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class ImpuestosDbDesignFactory : IDesignTimeDbContextFactory<ImpuestosDb>
{
    public ImpuestosDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ImpuestosDb>();
        ImpuestosDb.Configure(options, "Host=localhost;Database=diseno");
        return new ImpuestosDb(options.Options, new SinEmpresa());
    }

    private sealed class SinEmpresa : IPlatformTenantAccessor
    {
        public PlatformTenantContext Current => PlatformTenantContext.None;
    }
}
