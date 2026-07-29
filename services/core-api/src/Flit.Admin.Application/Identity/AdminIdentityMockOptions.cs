namespace Flit.Admin.Application.Identity;

/// <summary>
/// Interruptor de la SIMULACIÓN de validaciones de identidad administrativas (HU #11028). Existe para
/// poder probar la firma del mandato en ambientes no productivos, donde no hay forma de que el
/// mandatario complete una biométrica real contra el proveedor.
/// <para><b>Apagado por defecto y a propósito.</b> Una validación simulada satisface el gate que
/// habilita la firma del mandato: encenderlo en producción permitiría firmar mandatos sin que nadie
/// haya validado su identidad. El flag se activa por configuración explícita del ambiente
/// (<c>AdminIdentity:Mock:Enabled</c> / <c>ADMIN_IDENTITY_MOCK_ENABLED</c>), nunca por defecto.</para>
/// </summary>
public sealed class AdminIdentityMockOptions
{
    public const string SectionName = "AdminIdentity:Mock";

    /// <summary>¿Se permite simular validaciones de identidad en este ambiente? Default <c>false</c>.</summary>
    public bool Enabled { get; set; }
}
