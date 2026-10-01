using System.Security.Claims;
using System.Text.Json;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Tests.TestDoubles;
using Flit.Api.Endpoints;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Ocr;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Mandatos;

/// <summary>
/// HU #13161 — helpers únicos de los endpoints de mandatos: el mapeo de escrituras (<c>MapWrite</c>), la lectura
/// del usuario y la lista única de redacciones, compartidos por Plataforma y el hub del OT.
/// </summary>
public sealed class MandateEndpointHelpersTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    public static TheoryData<MandateConfigWriteStatus, int, string?> Escrituras => new()
    {
        { MandateConfigWriteStatus.Ok, 200, null },
        { MandateConfigWriteStatus.OfficeNotFound, 404, null },
        { MandateConfigWriteStatus.CompanyNotFound, 404, null },
        { MandateConfigWriteStatus.Conflict, 409, "row_version_conflict" },
        { MandateConfigWriteStatus.InvalidTemplate, 400, "template_code_invalido" },
        { MandateConfigWriteStatus.InvalidFamily, 400, "mandatary_family_invalida" },
        { MandateConfigWriteStatus.InvalidAssignmentMode, 400, "assignment_mode_invalido" },
        { MandateConfigWriteStatus.InstitutionalRequired, 400, "mandatario_institucional_requerido" },
        { MandateConfigWriteStatus.InvalidTemplateFile, 400, "plantilla_pdf_invalida" },
        { MandateConfigWriteStatus.InvalidEditorBody, 400, "editor_cuerpo_invalido" },
        { MandateConfigWriteStatus.InvalidDefaultSigner, 400, "mandatario_default_invalido" },
    };

    [Theory]
    [MemberData(nameof(Escrituras))]
    public void MapWrite_CadaResultadoMapeaAlMismoCodigoYError(
        MandateConfigWriteStatus status, int httpStatus, string? error)
    {
        var result = MandateEndpointHelpers.MapWrite(status, view: null);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(httpStatus);
        if (error is not null)
        {
            var json = JsonSerializer.Serialize(((IValueHttpResult)result).Value);
            JsonDocument.Parse(json).RootElement.GetProperty("error").GetString().Should().Be(error);
        }
    }

    [Fact]
    public void MapWrite_CubreTodosLosEstadosDelServicio()
    {
        var cubiertos = Escrituras.Select(r => r.Data.Item1).ToHashSet();

        Enum.GetValues<MandateConfigWriteStatus>().Should().BeSubsetOf(cubiertos,
            "un estado nuevo debe mapearse a propósito, no caer en el 400 sin cuerpo");
    }

    [Fact]
    public void ResolveUserId_LeeSubYCaeAlNameIdentifier()
    {
        var sub = Guid.NewGuid();
        var nameId = Guid.NewGuid();

        MandateEndpointHelpers.ResolveUserId(Principal(new Claim("sub", sub.ToString()))).Should().Be(sub);
        MandateEndpointHelpers.ResolveUserId(Principal(new Claim(ClaimTypes.NameIdentifier, nameId.ToString())))
            .Should().Be(nameId);
        MandateEndpointHelpers.ResolveUserId(Principal(new Claim("sub", "no-es-guid"))).Should().BeNull();
    }

    [Fact]
    public void ListaUnicaDeRedacciones_LaVistaPreviaYLaConfiguracionAceptanLoMismo()
    {
        MandatoTemplateResolver.PreviewableRedactions.Should().BeEquivalentTo(
            [MandatoTemplateResolver.Generico, MandatoTemplateResolver.Sabaneta,
             MandatoTemplateResolver.Bello, MandatoTemplateResolver.Municipio]);

        foreach (var code in MandatoTemplateResolver.PreviewableRedactions)
        {
            MandatoTemplateResolver.IsRedaction(code).Should().BeTrue();
            MandatoTemplateResolver.IsAcceptedTemplateCode(code).Should().BeTrue();
        }

        // «auto» se acepta al configurar el OT pero no es una redacción previsualizable.
        MandatoTemplateResolver.IsRedaction(MandatoTemplateResolver.Auto).Should().BeFalse();
        MandatoTemplateResolver.IsAcceptedTemplateCode(MandatoTemplateResolver.Auto).Should().BeTrue();
        MandatoTemplateResolver.IsAcceptedTemplateCode("inventada").Should().BeFalse();
        MandatoTemplateResolver.IsRedaction(" BELLO ").Should().BeTrue();
    }

    [Fact]
    public void RedaccionInvalida_PlataformaYOtResponden400ConLaMismaListaPermitida()
    {
        var result = MandatoFormatResponses.InvalidPreviewCode();

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(400);
        var json = JsonSerializer.Serialize(((IValueHttpResult)result).Value);
        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("error").GetString().Should().Be("template_code_invalido");
        root.GetProperty("allowed").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(MandatoTemplateResolver.PreviewableRedactions);
    }

    [Fact]
    public async Task UpsertAsync_ConTipoDeMandatoInventado_Responde400_YNoLoConvierteEnPersonaNatural()
    {
        await using var db = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase($"mandate-helpers-{Guid.NewGuid()}").Options);
        var officeId = Guid.NewGuid();
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(officeId).Returns(new TransitOfficeEntry(officeId, "5631000", "Sabaneta", "05", "05631"));
        var service = new MandateConfigAdminService(
            db, catalog, new StubTransitOfficeOperationalStatusReader(),
            Substitute.For<IDocumentOcrAnalyzer>(), Substitute.For<IMandateTemplateStorage>());

        var (status, view) = await service.UpsertAsync(
            officeId,
            new UpsertMandateOtConfigRequest(
                MandatoTemplateResolver.Generico, true, MandatoFamiliaCodes.Individuo,
                null, null, null, null, RowVersion: null, AssignmentMode: "inventado"),
            userId: null, Ct);

        status.Should().Be(MandateConfigWriteStatus.InvalidAssignmentMode);
        view.Should().BeNull();
        (await db.TransitOfficeMandateConfigs.CountAsync(Ct)).Should().Be(0);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
