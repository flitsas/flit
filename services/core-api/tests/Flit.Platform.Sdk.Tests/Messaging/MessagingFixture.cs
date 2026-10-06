using Flit.Platform.Sdk.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using Xunit;

namespace Flit.Platform.Sdk.Tests.Messaging;

/// <summary>
/// Postgres y RabbitMQ REALES, alcanzables por configuración (misma decisión que Flit.Integration.Tests: sin
/// Testcontainers). Postgres: el servidor de <c>ConnectionStrings__Core</c>, con una base efímera propia que se crea y se
/// borra. RabbitMQ: <c>FLIT_TEST_RABBITMQ</c> (en CI, el servicio del job). Sin alguno de los dos, las pruebas se omiten
/// en local y FALLAN en CI.
/// </summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    public const string Producer = "prueba";

    public string? SkipReason { get; private set; }

    public string Postgres { get; private set; } = string.Empty;

    public string RabbitMq { get; } = Environment.GetEnvironmentVariable("FLIT_TEST_RABBITMQ") ?? "amqp://flit:flit-prueba@127.0.0.1:5672/";

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core")
            ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        Postgres = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_sdk_{Guid.NewGuid():N}"[..30] }.ConnectionString;

        try
        {
            await using var db = NewDb();
            await db.Database.EnsureCreatedAsync();
            await using var connection = await new ConnectionFactory { Uri = new Uri(RabbitMq) }.CreateConnectionAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or RabbitMQ.Client.Exceptions.BrokerUnreachableException or System.Net.Sockets.SocketException)
        {
            await DropAsync();
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                throw;
            SkipReason = $"Postgres o RabbitMQ no alcanzables: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync() => await DropAsync();

    private async Task DropAsync()
    {
        try
        {
            await using var db = NewDb();
            await db.Database.EnsureDeletedAsync();
        }
        catch (NpgsqlException)
        {
            // Sin Postgres no hubo base que borrar.
        }
    }

    public PruebaDb NewDb() => new(new DbContextOptionsBuilder<PruebaDb>().UseNpgsql(Postgres).Options);

    public void SkipIfUnavailable() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
}

/// <summary>DbContext de un «servicio» de prueba: una tabla propia y la outbox en su esquema.</summary>
public sealed class PruebaDb(DbContextOptions<PruebaDb> options) : DbContext(options)
{
    public DbSet<Pedido> Pedidos => Set<Pedido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("prueba");
        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("pedidos");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.TenantId).HasColumnName("tenant_id");
            e.Property(p => p.Nombre).HasColumnName("nombre");
        });
        modelBuilder.AddFlitOutbox("prueba");
    }
}

public sealed class Pedido
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Nombre { get; set; } = string.Empty;
}
