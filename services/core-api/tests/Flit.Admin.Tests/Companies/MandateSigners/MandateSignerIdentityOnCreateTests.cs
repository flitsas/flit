using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11757 (ADR-0050) retiró el disparo antiguo del alta (<c>IAdminIdentityValidationService</c>). Con la HU #13246 el
/// disparo vuelve de forma acotada y SOLO por el puerto <c>IMandateSignerIdentityLauncher</c> (suite
/// <c>MandatarioIdentidadPropiaDisparoTests</c>). Aquí queda lo que no cambia: sin lanzador registrado el alta no crea ninguna
/// fila de validación ni envía correo, y con biometría el correo es obligatorio (422, campo <c>email</c>).
/// </summary>
public sealed class MandateSignerIdentityOnCreateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_WithEmail_DoesNotAttemptIdentityAndCreatesNoValidationRow()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var handler = Handler(ctx);

        var result = await handler.HandleAsync(Command("Con Correo", "111222", email: "mandatario@x.co"), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.NotAttempted);

        var signer = await ctx.MandateSigners.AsNoTracking()
            .FirstAsync(s => s.Id == result.MandateSignerId!.Value, Ct);
        // El correo se sigue capturando como dato de contacto (no se retira la persistencia del campo).
        signer.Email.Should().Be("mandatario@x.co");
    }

    [Fact]
    public async Task Create_WithBiometriaAndNoEmail_Is422OnEmailAndCreatesNothing()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var handler = Handler(ctx);

        var result = await handler.HandleAsync(Command("Sin Correo", "333444", email: null), Ct);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "email");
        (await ctx.MandateSigners.AnyAsync(Ct)).Should().BeFalse();
        (await ctx.ProcedureInstanceBiometricValidations.AnyAsync(Ct)).Should().BeFalse();
    }

    private static CreateMandateSignerCommand Command(
        string fullName, string documentNumber, string? email) =>
        new()
        {
            TransitOfficeId = MandateSignerHandlerTests.Office,
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
            FullName = fullName,
            DocumentNumber = documentNumber,
            DocumentType = "CC",
            Email = email,
            SignatureMethod = "biometria",
            CompanyTenantIds = [MandateSignerHandlerTests.CompanyA],
            CreatedBy = MandateSignerHandlerTests.Operator,
        };

    private static CreateMandateSignerHandler Handler(FlitDbContext ctx) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx));
}
