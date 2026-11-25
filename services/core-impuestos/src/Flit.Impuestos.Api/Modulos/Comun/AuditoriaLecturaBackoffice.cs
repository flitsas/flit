using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Comun;

/// <summary>
/// Deja en <c>impuestos.auditoria_lectura_backoffice</c> una fila por cada lectura del Super Admin sobre otra compañía (forma de
/// ADR-0073): quién (claim <c>sub</c>), qué compañía, qué recurso y cuándo. La fila es de la compañía consultada, así que se
/// guarda en un ámbito de escritura autorizado solo para ese tipo. Se escribe antes de leer: si no se puede auditar, la petición
/// falla y no se lee nada.
/// </summary>
/// <example>
/// <code>
/// await auditoria.RegistrarAsync(empresa, "liquidaciones", ct);
/// </code>
/// </example>
public sealed class AuditoriaLecturaBackoffice(ImpuestosDb db, UsuarioActual usuario, TimeProvider reloj)
{
    /// <summary>Largo de la columna <c>recurso</c>.</summary>
    public const int LargoMaximoRecurso = 64;

    public async Task RegistrarAsync(Guid empresaConsultada, string recurso, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recurso);
        if (empresaConsultada == Guid.Empty)
            throw new ArgumentException("La lectura auditada exige la empresa consultada.", nameof(empresaConsultada));
        usuario.EmpresaPropia();
        if (usuario.Id == Guid.Empty)
            throw ReglaException.Prohibido("SIN_USUARIO", "El token no identifica al usuario: la lectura no se puede auditar.");

        using (db.AutorizarEscrituraDe(empresaConsultada, typeof(LecturaBackoffice)))
        {
            db.Add(new LecturaBackoffice
            {
                ActorId = usuario.Id,
                TenantId = empresaConsultada,
                Recurso = recurso.Length > LargoMaximoRecurso ? recurso[..LargoMaximoRecurso] : recurso,
                En = reloj.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
