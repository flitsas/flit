namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// El <c>callbackUrl</c> de la liquidación de una empresa (HU #14711, AC-DC-07): solo si hay base y llave del token
/// (<see cref="OpcionesAvisoImpuesto.Activo"/>) y la empresa tiene secreto del webhook. Si no, null y la consulta se cierra por polling
/// (así se evita el 422 <c>webhook_not_configured</c> del proveedor). Si Identidad no responde, también null: el aviso es un
/// acelerador, nunca un requisito.
/// </summary>
/// <example>
/// <code>
/// var url = await callback.UrlAsync(tenantId, ct); // https://…/api/v1/impuesto/avisos/{token} o null
/// </code>
/// </example>
internal sealed class CallbackAvisoImpuesto(OpcionesAvisoImpuesto opciones, IFuenteSecretoWebhookImpuesto secretos)
{
    public async Task<string?> UrlAsync(Guid tenantId, CancellationToken ct)
    {
        if (!opciones.Activo)
            return null;
        try
        {
            var secreto = await secretos.ObtenerAsync(tenantId, forzar: false, ct).ConfigureAwait(false);
            if (!secreto.Configurado)
                return null;
        }
        catch (FalloSecretoWebhookImpuestoException)
        {
            return null;
        }

        return opciones.CallbackUrl(TokenAvisoImpuesto.Emitir(tenantId, opciones.Llave!));
    }
}
