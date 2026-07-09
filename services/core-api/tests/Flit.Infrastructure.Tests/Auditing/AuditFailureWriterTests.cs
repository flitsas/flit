using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Auditing;

/// <summary>
/// RNF01 (ADR-0024, sección 3): la auditoría de FALLOS se escribe en un scope/DbContext
/// PROPIO, así la traza <c>result = failure</c> sobrevive aunque la unidad de trabajo del cambio
/// principal haga rollback (no llegue a persistir). Se verifica con EF InMemory compartido: la
/// fila del cambio no guardada NO existe, pero la de fallo sí.
/// </summary>
public sealed class AuditFailureWriterTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FlitDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IAuditFailureWriter, AuditFailureWriter>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FailureRow_PersistsEvenWhenMainChangeRollsBack()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenantId = Guid.NewGuid();
        await using var provider = BuildProvider(nameof(FailureRow_PersistsEvenWhenMainChangeRollsBack));

        // Scope "principal": añade un cambio pero NUNCA lo guarda (simula rollback).
        using (var mainScope = provider.CreateScope())
        {
            var mainContext = mainScope.ServiceProvider.GetRequiredService<FlitDbContext>();
            mainContext.TenantOperationalPolicies.Add(new TenantOperationalPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            // El writer de fallo corre en su propio scope, independiente del cambio en vuelo.
            var writer = mainScope.ServiceProvider.GetRequiredService<IAuditFailureWriter>();
            await writer.WriteAsync(
                new AuditFailure(tenantId, "transit_office_profile", AuditVocabulary.Operations.Update,
                    "operation_mode", ClientIp: "198.51.100.9", ChangedBy: null),
                ct);

            // mainContext se descarta sin SaveChanges → el cambio principal "hace rollback".
        }

        await using var verifyContext = provider
            .CreateScope().ServiceProvider.GetRequiredService<FlitDbContext>();

        // El cambio principal no sobrevivió...
        (await verifyContext.TenantOperationalPolicies.AnyAsync(p => p.TenantId == tenantId, ct))
            .Should().BeFalse();

        // ...pero la traza de fallo sí, con su error_code y grano por operación.
        var failure = await verifyContext.TenantConfigAuditLogs.SingleAsync(ct);
        failure.Result.Should().Be(AuditVocabulary.Results.Failure);
        failure.Operation.Should().Be(AuditVocabulary.Operations.Update);
        failure.ErrorCode.Should().Be("operation_mode");
        failure.ClientIp.Should().Be("198.51.100.9");
        failure.EntityName.Should().Be("transit_office_profile");
    }
}
