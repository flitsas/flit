using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Catálogo OT desde <c>catalogs.transit_offices</c> (HU #10133 Fase 6).</summary>
internal sealed class DbTransitOfficeCatalog : ITransitOfficeCatalog
{
    private readonly FlitDbContext _context;

    public DbTransitOfficeCatalog(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IReadOnlyList<TransitOfficeEntry> All => Search(null);

    public IReadOnlyList<TransitOfficeEntry> Search(string? term)
    {
        var query = _context.TransitOffices.AsNoTracking().Where(o => o.IsActive);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var folded = Fold(term);
            query = query.Where(o =>
                EF.Functions.ILike(o.Name, $"%{term}%")
                || EF.Functions.ILike(o.Code, $"%{term}%")
                || Fold(o.Name).Contains(folded)
                || Fold(o.Code).Contains(folded));
        }

        return query
            .OrderBy(o => o.Name)
            .Select(o => new TransitOfficeEntry(o.Id, o.Code, o.Name, o.DepartmentCode, o.CityCode, o.CityName))
            .ToList();
    }

    public bool Exists(Guid id) =>
        _context.TransitOffices.AsNoTracking().Any(o => o.Id == id && o.IsActive);

    public TransitOfficeEntry? GetById(Guid id) =>
        _context.TransitOffices.AsNoTracking()
            .Where(o => o.Id == id && o.IsActive)
            .Select(o => new TransitOfficeEntry(o.Id, o.Code, o.Name, o.DepartmentCode, o.CityCode, o.CityName))
            .FirstOrDefault();

    private static string Fold(string value) =>
        value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(char.ToLowerInvariant(c)))
            .ToString();
}
