namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Rechazo de negocio al encender o apagar un producto. <see cref="Code"/> es estable para que el
/// endpoint de B-06 lo traduzca a RFC 7807 sin interpretar el mensaje.
/// </summary>
public sealed class TenantProductException : Exception
{
    /// <summary>El producto no existe en el catálogo (404).</summary>
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";

    /// <summary>Se pidió encender un producto inactivo en el catálogo (409).</summary>
    public const string ProductInactive = "PRODUCT_INACTIVE";

    /// <summary><c>plataforma</c> es el hub: siempre está encendido y no se habilita por empresa (400).</summary>
    public const string ProductAlwaysOn = "PRODUCT_ALWAYS_ON";

    /// <summary>Las notas pasan de <see cref="SetTenantProductEnabledHandler.NotesMaxLength"/> caracteres (400).</summary>
    public const string NotesTooLong = "PRODUCT_NOTES_TOO_LONG";

    public TenantProductException()
    {
        Code = string.Empty;
    }

    public TenantProductException(string message)
        : base(message)
    {
        Code = string.Empty;
    }

    public TenantProductException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = string.Empty;
    }

    public TenantProductException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>Código estable del rechazo (constantes de esta clase).</summary>
    public string Code { get; }
}
