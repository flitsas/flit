using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Ranking de adjuntos en el consolidado: tipos exactos de la prelación, con familia de
/// certificados de identidad por ordinal (múltiple propietario).
/// </summary>
internal static class ConsolidadoAttachmentRank
{
    internal static int Rank(
        string tipo,
        IReadOnlyDictionary<string, int> rankByTipo,
        int fallback)
    {
        if (rankByTipo.TryGetValue(tipo, out var exact))
            return exact;

        if (IdentityCertificateAttachmentTipo.IsIdentityCertificate(tipo))
        {
            var key = IdentityCertificateAttachmentTipo.RankKey(tipo);
            if (rankByTipo.TryGetValue(key, out var family))
                return family;
        }

        return fallback;
    }

    internal static IReadOnlyList<ProcedureInstanceAttachment> OrderByPrecedence(
        IEnumerable<ProcedureInstanceAttachment> attachments,
        IReadOnlyList<string> precedence,
        Func<ProcedureInstanceAttachment, bool> include)
    {
        var rank = precedence
            .Select((tipo, index) => (tipo, index))
            .ToDictionary(x => x.tipo, x => x.index, StringComparer.OrdinalIgnoreCase);

        var fallback = precedence.Count + 1;
        return attachments
            .Where(include)
            .OrderBy(a => Rank(a.Tipo, rank, fallback))
            .ThenBy(a => IdentityCertificateAttachmentTipo.IsIdentityCertificate(a.Tipo)
                ? IdentityCertificateAttachmentTipo.OrdinalFromTipo(a.Tipo)
                : 0)
            .ThenBy(a => a.UploadedAt)
            .ToList();
    }
}
