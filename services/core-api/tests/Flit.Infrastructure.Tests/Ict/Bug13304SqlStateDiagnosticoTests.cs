using Flit.Api.Grpc;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// Bug #13304 (review PR #536) — el log StepFailed de la materialización ICT incluye el SqlState de la
/// PostgresException interna (p. ej. 22001 = valor demasiado largo) y nunca el mensaje de la excepción.
/// <para>Uso de ejemplo: <c>IctOrchestrationService.SqlStateDe(ex)</c> → "22001" o "-".</para>
/// </summary>
public sealed class Bug13304SqlStateDiagnosticoTests
{
    [Fact]
    public void DbUpdateException_con_PostgresException_devuelve_el_SqlState()
    {
        var pg = new PostgresException("value too long for type character varying(200) 'PII'", "ERROR", "ERROR", "22001");

        IctOrchestrationService.SqlStateDe(new DbUpdateException("fallo", pg)).Should().Be("22001");
    }

    [Fact]
    public void Otra_excepcion_devuelve_guion() =>
        IctOrchestrationService.SqlStateDe(new InvalidOperationException("x")).Should().Be("-");

    [Fact]
    public void DbUpdateException_sin_PostgresException_devuelve_guion() =>
        IctOrchestrationService.SqlStateDe(new DbUpdateException("x", new TimeoutException())).Should().Be("-");
}
