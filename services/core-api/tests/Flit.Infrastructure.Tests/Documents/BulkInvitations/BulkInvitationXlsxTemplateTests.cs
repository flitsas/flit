using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Infrastructure.Documents.BulkInvitations;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.BulkInvitations;

/// <summary>
/// HU #14422 — plantilla con catálogo de <see cref="IInvitationScopePolicy"/>.
/// Uso de ejemplo: <c>await template.BuildAsync(context, ct)</c>.
/// </summary>
public sealed class BulkInvitationXlsxTemplateTests
{
    private readonly IInvitationScopePolicy _scope = Substitute.For<IInvitationScopePolicy>();
    private readonly BulkInvitationXlsxTemplate _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();

    public BulkInvitationXlsxTemplateTests()
    {
        _sut = new BulkInvitationXlsxTemplate(_scope, new BulkInvitationOptions());
    }

    [Fact]
    public async Task BuildAsync_PermittedContext_IncludesAssignableRolesAndRequiredHeaders()
    {
        _scope.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>())
            .Returns(InvitationScopeDecision.Permitted(
                InvitationTargetKind.Company,
                [
                    new InvitationRoleOption(RoleId, "Radicador", "Radicador", true, null),
                    new InvitationRoleOption(Guid.NewGuid(), "SuperAdmin", "SuperAdmin", false, InvitationScopeReasons.SuperAdminRoleNotAssignable),
                ]));

        var rendered = await _sut.BuildAsync(
            new InvitationScopeContext(UserId, TenantId, ["AdminCompany"], TenantId, InvitationChannel.Security),
            CancellationToken.None);

        rendered.Filename.Should().EndWith(".xlsx");
        rendered.Content.Should().NotBeEmpty();

        using var doc = SpreadsheetDocument.Open(new MemoryStream(rendered.Content), false);
        var sheets = doc.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value).ToList();
        sheets.Should().Contain(BulkInvitationTemplateCatalog.SheetName);
        sheets.Should().Contain(BulkInvitationTemplateCatalog.ListsSheetName);

        var dataPart = (WorksheetPart)doc.WorkbookPart.GetPartById(
            doc.WorkbookPart.Workbook.Sheets!.Elements<Sheet>().First(s => s.Name == BulkInvitationTemplateCatalog.SheetName).Id!);
        using var reader = OpenXmlReader.Create(dataPart);
        Row? header = null;
        while (reader.Read())
        {
            if (reader.ElementType == typeof(Row) && reader.IsStartElement)
            {
                header = (Row)reader.LoadCurrentElement()!;
                break;
            }
        }

        header.Should().NotBeNull();
        var texts = header!.Elements<Cell>()
            .Select(c => c.InlineString?.Text?.Text ?? string.Empty)
            .ToList();
        texts.Should().ContainInOrder(
            BulkInvitationTemplateCatalog.FullNameHeader,
            BulkInvitationTemplateCatalog.EmailHeader,
            BulkInvitationTemplateCatalog.RoleHeader);
    }

    [Fact]
    public async Task BuildAsync_ScopeDenied_ThrowsWithoutFile()
    {
        _scope.ResolveAsync(Arg.Any<InvitationScopeContext>(), Arg.Any<CancellationToken>())
            .Returns(InvitationScopeDecision.Denied(InvitationScopeReasons.InviterNotAuthorized));

        var act = () => _sut.BuildAsync(
            new InvitationScopeContext(UserId, TenantId, ["Radicador"], TenantId, InvitationChannel.Security),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvitationScopeDeniedException>()
            .Where(ex => ex.Reason == InvitationScopeReasons.InviterNotAuthorized);
    }
}
