namespace Flit.Tramites.Domain.ExternalSync;

/// <summary>
/// HU #13081 — enmascarado de los datos personales de los compradores para un cliente sin el permiso
/// <c>external.tramites.pii.read</c> (contrato v3.1 §4). Los campos llegan enmascarados, nunca ausentes; un
/// <c>null</c> sigue siendo <c>null</c> (enmascarar no inventa un dato). Reglas aprobadas por el PO:
/// <list type="bullet">
///   <item>documento: primer carácter y últimos 4 (<c>9****0000</c>);</item>
///   <item>correo: primera letra y dominio (<c>c***@ejemplo.test</c>);</item>
///   <item>celular: últimos 4 (<c>******0000</c>);</item>
///   <item>nombre: inicial de cada palabra (<c>P*** E***</c>);</item>
///   <item>dirección: oculta entera (<c>***</c>).</item>
/// </list>
/// </summary>
public static class ProcedureSyncPiiMasker
{
    private const string Oculto = "***";

    public static ProcedureSyncItem Mask(ProcedureSyncItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item with
        {
            Compradores = item.Compradores
                .Select(c => c with
                {
                    NumeroDocumento = Documento(c.NumeroDocumento),
                    NombreCompleto = Nombre(c.NombreCompleto),
                    Direccion = c.Direccion is null ? null : Oculto,
                    Celular = Celular(c.Celular),
                    Correo = Correo(c.Correo),
                })
                .ToList(),
        };
    }

    public static string? Documento(string? valor)
    {
        if (valor is null)
        {
            return null;
        }

        return valor.Length <= 5
            ? new string('*', valor.Length)
            : valor[0] + new string('*', valor.Length - 5) + valor[^4..];
    }

    public static string? Celular(string? valor)
    {
        if (valor is null)
        {
            return null;
        }

        return valor.Length <= 4 ? new string('*', valor.Length) : new string('*', valor.Length - 4) + valor[^4..];
    }

    public static string? Correo(string? valor)
    {
        if (valor is null)
        {
            return null;
        }

        var arroba = valor.IndexOf('@', StringComparison.Ordinal);
        return arroba <= 0 ? Oculto : valor[0] + Oculto + valor[arroba..];
    }

    public static string? Nombre(string? valor) =>
        valor is null
            ? null
            : string.Join(' ', valor.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0] + Oculto));
}
