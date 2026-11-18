using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Un scope de servicios fijado a la empresa explícita del backoffice (HU #14576, D-81): su <c>DiagnosticoDb</c>, su almacén y sus
/// lectores filtran y escriben con esa empresa, como el trabajador del ZIP con <see cref="EmpresaDelTrabajo"/>. Lo usa la gestión del
/// saneamiento para lo que no es una fila del saneamiento: guardar y leer el PDF de la evidencia en el almacén (SP-12, también en el
/// respaldo en la base) y armar el panel de deuda con la misma proyección del cliente (SP-7, SP-11). Solo se abre después de validar
/// la cadena empresa → saneamiento, y nunca escribe saneamientos (esos van por <see cref="EscrituraEntreEmpresas"/>).
/// </summary>
internal static class AmbitoDeLaEmpresa
{
    public static AsyncServiceScope Abrir(IServiceScopeFactory scopes, Guid empresa)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EmpresaDelTrabajo>().Fijar(empresa);
        return scope;
    }
}
