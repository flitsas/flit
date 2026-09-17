using Flit.Tramites.Application.BulkTramites.Parsing;

namespace Flit.Tramites.Application.BulkTramites;

/// <summary>Puerto del parser XLSX de carga masiva de trámites (HU #12522, AC1/AC2/AC3).</summary>
public interface IBulkTramitesXlsxParser
{
    BulkTramitesParseResult Parse(BulkTramitesTemplateType tipo, Stream xlsx);
}
