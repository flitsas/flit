using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Admin.Tests.Companies.MandateSigners;

/// <summary>
/// HU #11757 (ADR-0050) — el alta de un mandatario YA NO dispara la validación de identidad. Esta
/// suite reemplaza a la de la HU #11000 (que ejercitaba el apalancamiento vía
/// <c>IAdminIdentityValidationService</c>): ahora prueba lo contrario — que el alta, CON o SIN correo,
/// no crea ninguna fila de validación y no envía ningún correo. El módulo Identidad es la única fuente
/// que puede originar una validación (ADR-0050).
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

        var validaciones = await ctx.AdminIdentityValidations.AsNoTracking().CountAsync(Ct);
        validaciones.Should().Be(0);

        var signer = await ctx.MandateSigners.AsNoTracking()
            .FirstAsync(s => s.Id == result.MandateSignerId!.Value, Ct);
        signer.IdentityValidationRef.Should().BeNull();
        // El correo se sigue capturando como dato de contacto (no se retira la persistencia del campo).
        signer.Email.Should().Be("mandatario@x.co");
    }

    [Fact]
    public async Task Create_WithoutEmail_DoesNotAttemptIdentityAndCreatesNoValidationRow()
    {
        await using var ctx = MandateSignerHandlerTests.NewSeededContext();
        var handler = Handler(ctx);

        var result = await handler.HandleAsync(Command("Sin Correo", "333444", email: null), Ct);

        result.IsValid.Should().BeTrue();
        result.Identity.Should().Be(MandateSignerIdentityOutcome.NotAttempted);

        var validaciones = await ctx.AdminIdentityValidations.AsNoTracking().CountAsync(Ct);
        validaciones.Should().Be(0);

        var signer = await ctx.MandateSigners.AsNoTracking()
            .FirstAsync(s => s.Id == result.MandateSignerId!.Value, Ct);
        signer.IdentityValidationRef.Should().BeNull();
        signer.Email.Should().BeNull();
    }

    private static CreateMandateSignerCommand Command(
        string fullName, string documentNumber, string? email) =>
        new()
        {
            TransitOfficeId = MandateSignerHandlerTests.Office,
            FullName = fullName,
            DocumentNumber = documentNumber,
            DocumentType = "CC",
            Email = email,
            CompanyTenantIds = [MandateSignerHandlerTests.CompanyA],
            CreatedBy = MandateSignerHandlerTests.Operator,
        };

    private static CreateMandateSignerHandler Handler(FlitDbContext ctx) =>
        new(
            new DbTransitOfficeOperationalStatusReader(ctx),
            new DbMandateSignerReader(ctx),
            new MandateSignerRepository(ctx));
}
