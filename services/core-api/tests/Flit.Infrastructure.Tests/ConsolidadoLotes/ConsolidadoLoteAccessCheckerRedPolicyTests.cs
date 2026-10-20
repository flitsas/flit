using System.Text.RegularExpressions;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Security;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Flit.Infrastructure.Tests.ConsolidadoLotes;

/// <summary>
/// HU #13418 — code review Obs1: en un lote de red, la decisión «la clase de la cabeza puede leer documentos de su red»
/// de <see cref="ConsolidadoLoteAccessChecker"/> es la de <see cref="NetworkDocumentsPolicy.ValidateKind"/> (regla única,
/// fail-closed ante una clase nueva), no una copia de la condición. Tabla de verdad MARCA_BLANCA/CONCESIÓN × interruptor
/// <c>network_documents_concesion</c> sobre <see cref="FlitDbContext"/> InMemory, y un guardián de fuente que falla si
/// el checker vuelve a decidir con <c>GroupKind.Concesion</c> en lugar de la política.
/// <para>Uso de ejemplo:
/// <c>await new ConsolidadoLoteAccessChecker(db, cache).TieneAccesoAsync(ctxDeUnItemDeLaHija, ct)</c> ⇒ igual a
/// <c>NetworkDocumentsPolicy.ValidateKind(TenantScope.Group(P, [C1], clase), interruptor) is null</c>.</para>
/// </summary>
public sealed class ConsolidadoLoteAccessCheckerRedPolicyTests : IDisposable
{
    private static readonly Guid P = Guid.NewGuid();
    private static readonly Guid C1 = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid RolDescarga = Guid.NewGuid();
    private static readonly Guid RolAdminCompany = Guid.NewGuid();
    private static readonly Guid Permiso = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(GroupKind.MarcaBlanca, false, true)]
    [InlineData(GroupKind.MarcaBlanca, true, true)]
    [InlineData(GroupKind.Concesion, false, false)]
    [InlineData(GroupKind.Concesion, true, true)]
    public async Task Obs1_ItemDeLaHija_LaDecisionDeLaClaseSaleDeNetworkDocumentsPolicy(
        GroupKind clase, bool interruptor, bool esperado)
    {
        var politica = NetworkDocumentsPolicy.ValidateKind(TenantScope.Group(P, [C1], clase), interruptor) is null;
        politica.Should().Be(esperado, "la tabla de verdad es la de la política de documentos de red");

        await using var db = await SeedAsync(clase, interruptor);
        var tramite = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = C1,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-13418",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ProcedureInstances.Add(tramite);
        await db.SaveChangesAsync(Ct);

        var ctx = new LoteItemContexto(Guid.NewGuid(), Guid.NewGuid(), ConsolidadoExportOrigin.Tramites, P, C1, null,
            Usuario, NetworkScopePolicy.HeadAdminRole, tramite.Id, ConsolidadoExportDocumentType.Consolidado, RedActiva: true);

        var acceso = await new ConsolidadoLoteAccessChecker(db, _cache).TieneAccesoAsync(ctx, Ct);

        acceso.Should().Be(politica, $"cabeza {clase} con network_documents_concesion = {interruptor}");
    }

    [Fact]
    public void Obs1_ElCheckerNoCopiaLaCondicionDeClase_DelegaEnLaPolitica()
    {
        var fuente = File.ReadAllText(Path.Combine(
            RepoRoot(), "services", "core-api", "src", "Flit.Infrastructure", "Security", "ConsolidadoLoteAccessChecker.cs"));

        fuente.Should().Contain("NetworkDocumentsPolicy.ValidateKind(",
            "la regla de documentos de red es una sola: NetworkDocumentsPolicy (fail-closed ante una clase nueva)");
        Regex.IsMatch(fuente, @"GroupKind\.(Concesion|MarcaBlanca)\b").Should().BeFalse(
            "una comparación directa con la clase de la cabeza reintroduce la copia de la regla (Obs1)");
    }

    private async Task<FlitDbContext> SeedAsync(GroupKind clase, bool interruptor)
    {
        var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(_dbName).Options);
        var codigoClase = clase == GroupKind.Concesion ? GroupKindCodes.Concesion : GroupKindCodes.MarcaBlanca;
        db.Tenants.AddRange(
            new Tenant { Id = P, Code = "P13418", LegalName = "Cabeza", TaxId = "900000001", TenantType = codigoClase, IsGroupParent = true },
            new Tenant { Id = C1, Code = "C13418", LegalName = "Hija", TaxId = "900000002", TenantType = "CONCESIONARIO", ParentTenantId = P });
        db.HierarchySwitches.AddRange(
            new HierarchySwitch { Id = Guid.NewGuid(), SwitchKey = HierarchySwitch.GroupReadScopeKey, IsEnabled = true },
            new HierarchySwitch { Id = Guid.NewGuid(), SwitchKey = HierarchySwitch.NetworkDocumentsConcesionKey, IsEnabled = interruptor });
        db.Users.Add(new User { Id = Usuario, Email = "lote13418@it.test", DisplayName = "Admin cabeza", Status = "active" });
        db.Roles.AddRange(
            new Role { Id = RolDescarga, Code = "Gestor", Name = "Gestor", IsActive = true },
            new Role { Id = RolAdminCompany, Code = NetworkScopePolicy.HeadAdminRole, Name = "Administrador", IsActive = true, ProductCode = "plataforma" });
        db.RbacActions.Add(new RbacAction { Id = Permiso, ModuleId = Guid.NewGuid(), Slug = ConsolidadoLotePermisos.Descargar, Name = "Descargar", IsActive = true });
        db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), RoleId = RolDescarga, PermissionId = Permiso });
        db.UserRoleAssignments.AddRange(Asignacion(RolDescarga), Asignacion(RolAdminCompany));
        await db.SaveChangesAsync(Ct);
        return db;
    }

    private static UserRoleAssignment Asignacion(Guid rol) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Usuario,
        RoleId = rol,
        TenantId = P,
        AssignedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.prod.yml")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
