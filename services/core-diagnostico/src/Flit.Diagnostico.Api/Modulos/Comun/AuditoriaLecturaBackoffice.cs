using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Deja en <c>diagnostico.lecturas_backoffice</c> una fila por cada lectura entre compañías del backoffice FLIT (HU #13728,
/// patrón ADR-0069): usuario (claim <c>sub</c>), empresa consultada, ruta con su consulta (sin cuerpo) y fecha (la pone la
/// base). La fila es de la compañía del lector, así que se guarda por la vía normal y <c>TenantFilter.ValidarEscritura</c>
/// la acepta. Se escribe antes de leer: si no se puede auditar, la petición falla y no se lee nada.
/// </summary>
public sealed class AuditoriaLecturaBackoffice(DiagnosticoDb db, UsuarioActual usuario)
{
    /// <summary>Largo de la columna <c>ruta</c> (<c>LecturaBackofficeConfig</c>).</summary>
    public const int LargoMaximoRuta = 500;

    public Task RegistrarAsync(Guid empresaConsultada, HttpRequest request, CancellationToken ct) =>
        RegistrarVariasAsync([empresaConsultada], request, ct);

    /// <summary>
    /// Una fila por cada empresa distinta de <paramref name="empresasConsultadas"/>, en un solo <c>SaveChanges</c> (HU #14576, SP-4: la
    /// bandeja entre compañías audita las empresas de la página antes de responder; si falla, la petición falla sin filas). Sin
    /// empresas no escribe nada, pero exige igual un usuario identificado.
    /// </summary>
    public async Task RegistrarVariasAsync(IEnumerable<Guid> empresasConsultadas, HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(empresasConsultadas);
        ArgumentNullException.ThrowIfNull(request);
        usuario.EmpresaPropia();
        if (usuario.Id == Guid.Empty)
            throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: la lectura no se puede auditar.");

        var ruta = $"{request.PathBase}{request.Path}{request.QueryString}";
        var empresas = empresasConsultadas.Distinct().ToList();
        if (empresas.Count == 0)
            return;
        foreach (var empresa in empresas)
        {
            db.Add(new LecturaBackoffice
            {
                EmpresaConsultada = empresa,
                UsuarioId = usuario.Id,
                Ruta = ruta.Length > LargoMaximoRuta ? ruta[..LargoMaximoRuta] : ruta,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
