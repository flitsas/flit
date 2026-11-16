using Flit.Infrastructure.Documents.BulkInvitations;
using Flit.Modules.Security.Application.Auth.BulkInvitation;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.BulkInvitations;

/// <summary>
/// HU #14422 — lectura SAX del .xlsx de invitación masiva.
/// Uso de ejemplo: <c>new BulkInvitationXlsxParser(options).Parse(stream)</c>.
/// </summary>
public sealed class BulkInvitationXlsxParserTests
{
    private readonly BulkInvitationXlsxParser _sut = new(new BulkInvitationOptions { MaxRows = 50, MaxFileBytes = 1024 * 1024 });

    [Fact]
    public void Parse_ValidFileWithBlankRow_ReturnsNormalizedDataRows()
    {
        var bytes = BulkInvitationXlsxBuilder.Build(
            BulkInvitationTemplateCatalog.Headers,
            [
                [" Ana Pérez ", " ana@flit.local ", " Radicador "],
                ["", "", ""],
                ["Bob", "bob@flit.local", "Radicador"],
                ["Carol", "carol@flit.local", "Radicador"],
            ]);

        var result = _sut.Parse(new MemoryStream(bytes));

        result.FileError.Should().BeNull();
        result.Rows.Should().HaveCount(3);
        result.Rows[0].FullName.Should().Be("Ana Pérez");
        result.Rows[0].Email.Should().Be("ana@flit.local");
        result.Rows[0].Role.Should().Be("Radicador");
        result.Rows[0].RowNumber.Should().Be(2);
        result.Rows[1].RowNumber.Should().Be(4);
        result.Rows[2].RowNumber.Should().Be(5);
    }

    [Fact]
    public void Parse_WrongHeaders_RejectsTemplateInvalid()
    {
        var bytes = BulkInvitationXlsxBuilder.WithHeaders(["Nombre", "Mail", "Perfil"]);

        var result = _sut.Parse(new MemoryStream(bytes));

        result.FileError.Should().Be(BulkInvitationFileErrors.TemplateInvalid);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NotZip_RejectsInvalidFile()
    {
        var result = _sut.Parse(new MemoryStream("not-an-xlsx"u8.ToArray()));

        result.FileError.Should().Be(BulkInvitationFileErrors.InvalidFile);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TooManyRows_RejectsEntireFile()
    {
        var rows = Enumerable.Range(0, 51)
            .Select(i => new[] { $"User {i}", $"u{i}@flit.local", "Radicador" })
            .ToArray();
        var bytes = BulkInvitationXlsxBuilder.Build(BulkInvitationTemplateCatalog.Headers, rows);

        var result = _sut.Parse(new MemoryStream(bytes));

        result.FileError.Should().Be(BulkInvitationFileErrors.TooManyRows);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_FormulaCell_MarksFormulaNeutralized()
    {
        var bytes = BulkInvitationXlsxBuilder.WithRows(("=HYPERLINK(\"x\")", "safe@flit.local", "Radicador"));

        var result = _sut.Parse(new MemoryStream(bytes));

        result.FileError.Should().BeNull();
        result.Rows.Should().ContainSingle();
        result.Rows[0].FormulaNeutralized.Should().BeTrue();
        result.Rows[0].FullName.Should().StartWith("'=");
    }

    [Fact]
    public void Parse_HeaderOnly_RejectsEmptyFile()
    {
        var bytes = BulkInvitationXlsxBuilder.WithHeaders(BulkInvitationTemplateCatalog.Headers);

        var result = _sut.Parse(new MemoryStream(bytes));

        result.FileError.Should().Be(BulkInvitationFileErrors.EmptyFile);
    }
}
