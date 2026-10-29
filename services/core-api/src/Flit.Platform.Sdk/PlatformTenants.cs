namespace Flit.Platform.Sdk;

/// <summary>Empresas de la plataforma con id fijo (contrato de plataforma §7).</summary>
public static class PlatformTenants
{
    /// <summary>
    /// HU #13359 (Epic #13316): lo que es de la plataforma y no de una empresa — simulación de mandato, buzón de pruebas,
    /// recuperación de contraseña de un usuario sin rol, reportes de alcance SuperAdmin — viaja con esta empresa, porque
    /// todo trabajo y toda llamada entre servicios llevan una. No existe como fila en <c>admin.companies</c>.
    /// </summary>
    public static readonly Guid Plataforma = new("00000000-0000-0000-0000-0000000f1170");

    /// <summary>
    /// Épica #14021 (HU #14033): la empresa a cuyo nombre nacen todos los trámites del portal de persona natural. A
    /// diferencia de <see cref="Plataforma"/>, esta SÍ es una fila de <c>identity.tenants</c> (la siembra el DDL 152): un
    /// trámite necesita una empresa real. <c>core-api</c> solo acepta que el servicio del portal actúe para esta
    /// empresa y para ninguna otra.
    /// </summary>
    public static readonly Guid PersonaNatural = new("00000000-0000-0000-0000-0000000f1171");

    /// <summary>La empresa de un envío: la del mensaje, o <see cref="Plataforma"/> si no tiene.</summary>
    public static Guid O(Guid? tenantId) => tenantId is { } t && t != Guid.Empty ? t : Plataforma;
}
