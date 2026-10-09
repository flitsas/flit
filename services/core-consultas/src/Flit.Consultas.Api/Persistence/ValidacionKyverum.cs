namespace Flit.Consultas.Api.Persistence;

/// <summary>
/// HU #13351 — una validación de identidad creada en Kyverum Verify por pedido de un servicio. Guarda el secreto con el
/// que Kyverum firma sus avisos (cifrado con Data Protection) y a quién pertenece, para verificar y enrutar el aviso.
/// </summary>
public sealed class ValidacionKyverum
{
    /// <summary>Id de la validación en el servicio que la pidió (va en la URL del aviso).</summary>
    public Guid ValidacionId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Servicio que la pidió (producto del token de servicio).</summary>
    public string Producto { get; set; } = string.Empty;

    public string VerificationId { get; set; } = string.Empty;

    /// <summary>Null si Kyverum no devolvió secreto: el aviso no se puede verificar y se rechaza.</summary>
    public string? SecretoCifrado { get; set; }

    public DateTimeOffset CreadaEn { get; set; }
}
