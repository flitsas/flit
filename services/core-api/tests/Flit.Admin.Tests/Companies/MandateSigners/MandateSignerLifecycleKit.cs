using System.Text.Json;
using Flit.Admin.Application.Companies.MandateSigners.DeleteMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.GetMandateSignerImpact;
using Flit.Admin.Application.Companies.MandateSigners.InactivateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.ReactivateMandateSigner;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// Semillas y fábricas compartidas por las pruebas del ciclo de vida del mandatario (HU #13135–#13138) sobre InMemory:
/// handlers y repositorio reales, con un reasignador falso opcional.
/// </summary>
internal static class MandateSignerLifecycleKit
{
    internal static readonly Guid Office = MandateSignerHandlerTests.Office;
    internal static readonly Guid Office2 = Guid.Parse("0ff1ce00-0000-4000-8000-000000000002");
    internal static readonly Guid CompanyA = MandateSignerHandlerTests.CompanyA;
    internal static readonly Guid CompanyB = MandateSignerHandlerTests.CompanyB;
    internal static readonly Guid CompanyC = MandateSignerHandlerTests.CompanyC;
    internal static readonly Guid Operator = MandateSignerHandlerTests.Operator;

    internal static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Semillas ────────────────────────────────────────────────────────────────────────────────

    internal static Guid AddSigner(
        FlitDbContext ctx,
        string name,
        (Guid Office, Guid Company, string Scope)[] links,
        bool active = true,
        Guid? primaryOffice = null)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        ctx.MandateSigners.Add(new MandateSigner
        {
            Id = id,
            TransitOfficeId = primaryOffice ?? Office,
            FullName = name,
            DocumentType = "CC",
            DocumentNumber = "9988776655",
            IntegrityHash = new string('a', 64),
            RegisteredAt = now,
            IsActive = active,
            SignerModel = "juridica",
            CreatedAt = now,
        });

        foreach (var officeId in links.Select(l => l.Office).Append(primaryOffice ?? Office).Distinct())
        {
            ctx.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
            {
                Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = officeId, IsActive = active, CreatedAt = now,
            });
        }

        foreach (var (office, company, scope) in links)
        {
            ctx.MandateSignerCompanies.Add(new MandateSignerCompany
            {
                Id = Guid.NewGuid(), MandateSignerId = id, TransitOfficeId = office, CompanyTenantId = company,
                IsActive = active, ConfiguredByScope = scope, CreatedAt = now,
            });
        }

        ctx.SaveChanges();
        return id;
    }

    internal static void SetRuleDefault(FlitDbContext ctx, Guid office, Guid company, Guid? signerId)
    {
        var rule = ctx.CompanyOtMandateRules.FirstOrDefault(r => r.TransitOfficeId == office && r.CompanyTenantId == company);
        if (rule is null)
        {
            ctx.CompanyOtMandateRules.Add(new CompanyOtMandateRuleEntity
            {
                Id = Guid.NewGuid(), CompanyTenantId = company, TransitOfficeId = office,
                DefaultMandateSignerId = signerId, CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            rule.DefaultMandateSignerId = signerId;
        }

        ctx.SaveChanges();
    }

    internal static void SetOfficeDefault(FlitDbContext ctx, Guid office, Guid? signerId)
    {
        var config = ctx.TransitOfficeMandateConfigs.FirstOrDefault(c => c.TransitOfficeId == office);
        if (config is null)
        {
            ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
            {
                Id = Guid.NewGuid(), TransitOfficeId = office, DefaultMandateSignerId = signerId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            config.DefaultMandateSignerId = signerId;
        }

        ctx.SaveChanges();
    }

    internal static Guid AddPending(FlitDbContext ctx, Guid signerId, string status = TramiteEstado.Entregado)
    {
        var instance = new ProcedureInstance
        {
            ProcedureType = ProcedureTypeFixture.Matricula,
            Id = Guid.NewGuid(),
            TenantId = CompanyA,
            Status = status,
            TransitOfficeId = Office,
            MandateSignerId = signerId,
        };
        ctx.ProcedureInstances.Add(instance);
        ctx.SaveChanges();
        return instance.Id;
    }

    internal sealed class FakeReassigner(MandateSignerReassignmentResult result) : IMandateSignerProcedureReassigner
    {
        public int Calls { get; private set; }

        public Task<MandateSignerReassignmentResult> ReassignAsync(Guid mandateSignerId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    internal sealed record Sut(
        FlitDbContext Ctx,
        DeleteMandateSignerHandler Delete,
        InactivateMandateSignerHandler Inactivate,
        ReactivateMandateSignerHandler Reactivate,
        GetMandateSignerImpactHandler Impact,
        ListMandateSignersHandler List);

    internal static Sut Build(FlitDbContext ctx, IMandateSignerProcedureReassigner? reassigner = null)
    {
        var otStatus = new DbTransitOfficeOperationalStatusReader(ctx);
        var reader = new DbMandateSignerReader(ctx);
        var impactReader = new DbMandateSignerImpactReader(ctx);
        var repo = new MandateSignerRepository(ctx, reassigner);
        return new Sut(
            ctx,
            new DeleteMandateSignerHandler(otStatus, reader, impactReader, repo),
            new InactivateMandateSignerHandler(otStatus, reader, repo),
            new ReactivateMandateSignerHandler(otStatus, reader, repo),
            new GetMandateSignerImpactHandler(reader, impactReader),
            new ListMandateSignersHandler(reader));
    }

    internal static DeleteMandateSignerCommand Del(Guid id, bool confirm = false, MandateSignerActorKind actor = MandateSignerActorKind.OtAdmin) =>
        new() { TransitOfficeId = Office, MandateSignerId = id, ConfirmImpact = confirm, ChangedBy = Operator, ActorKind = actor };

    internal static InactivateMandateSignerCommand Off(Guid id, MandateSignerActorKind actor = MandateSignerActorKind.OtAdmin) =>
        new() { TransitOfficeId = Office, MandateSignerId = id, ChangedBy = Operator, ActorKind = actor };

    internal static ReactivateMandateSignerCommand On(Guid id, MandateSignerActorKind actor = MandateSignerActorKind.OtAdmin) =>
        new() { TransitOfficeId = Office, MandateSignerId = id, ChangedBy = Operator, ActorKind = actor };

    internal static List<TenantConfigAuditLog> Lifecycle(FlitDbContext ctx) =>
        [.. ctx.TenantConfigAuditLogs.Where(l => l.Module == "mandatarios").OrderBy(l => l.ChangedAt)];
}
