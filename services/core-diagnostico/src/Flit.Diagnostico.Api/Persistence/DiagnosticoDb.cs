using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flit.Diagnostico.Api.Persistence;

/// <summary>
/// Base del servicio: SOLO su esquema (<c>diagnostico</c>), del que es dueño su usuario de base (HU #13330). Nace con la
/// outbox y la bandeja de entrada del SDK; las tablas propias se agregan aquí con su migración.
/// </summary>
public sealed class DiagnosticoDb(DbContextOptions<DiagnosticoDb> options) : DbContext(options)
{
    public const string Schema = ServicioSettings.Codigo;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddFlitOutbox(Schema);
        modelBuilder.AddFlitInbox(Schema);
    }

    /// <summary>Opciones comunes: Npgsql con la tabla de migraciones dentro del propio esquema.</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema);
            npgsql.EnableRetryOnFailure(3);
        });
}

/// <summary>Para <c>dotnet ef migrations add</c>: no necesita una base de verdad.</summary>
internal sealed class DiagnosticoDbDesignFactory : IDesignTimeDbContextFactory<DiagnosticoDb>
{
    public DiagnosticoDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DiagnosticoDb>();
        DiagnosticoDb.Configure(options, "Host=localhost;Database=diseno");
        return new DiagnosticoDb(options.Options);
    }
}
