using System.Text;
using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #11364 AC2 — columna <c>admin.notification_delivery_logs.recipient_diverted</c> (DDL 68).
/// Mismo patrón que <c>NotificationDeliveryLogSchemaTests</c>: verificación estática del script
/// embebido, sin Postgres real en la suite.
/// </summary>
public sealed class NotificationDeliveryLogRecipientDivertedSchemaTests
{
    private const string DdlResource =
        "Flit.Infrastructure.Persistence.Sql.Ddl.68-notification-delivery-log-recipient-diverted.sql";

    private static string Load()
    {
        var assembly = typeof(FlitDbContext).Assembly;
        using var stream = assembly.GetManifestResourceStream(DdlResource);
        stream.Should().NotBeNull($"el DDL embebido {DdlResource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Statements() =>
        Regex.Replace(Load(), @"(?m)^\s*--.*$", string.Empty);

    [Fact]
    public void AgregaLaColumnaBooleanaConDefaultFalso()
    {
        Load().Should().Contain(
            "ADD COLUMN IF NOT EXISTS recipient_diverted boolean NOT NULL DEFAULT false");
    }

    [Fact]
    public void EsIdempotente()
    {
        var sql = Statements();

        sql.Should().Contain("ADD COLUMN IF NOT EXISTS");
        sql.Should().NotMatchRegex(@"ADD COLUMN (?!IF NOT EXISTS)");
    }

    [Fact]
    public void LlevaComentarioQueExplicaQueElEnvioNoLlegoAlDestinatarioDeLaFila()
    {
        var comentario = Regex.Match(
            Load(),
            @"COMMENT ON COLUMN admin\.notification_delivery_logs\.recipient_diverted IS\s*'([^']*)'");

        comentario.Success.Should().BeTrue("recipient_diverted debe llevar COMMENT ON COLUMN");
        comentario.Groups[1].Value.Should().Contain(
            "NO llegó", "sin esta advertencia la fila desviada parecería una entrega normal");
    }
}
