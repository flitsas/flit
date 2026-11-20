using System.Net.Mail;
using System.Text.RegularExpressions;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Plataforma.PersonaNatural;

namespace Flit.Admin.Application.Plataforma.PersonaNatural;

/// <summary>Códigos de error estables de la configuración del portal.</summary>
public static class ConfiguracionPnErrores
{
    public const string ValorFueraDeRango = "valor_fuera_de_rango";
    public const string CorreoInvalido = "correo_invalido";
    public const string TelefonoInvalido = "telefono_invalido";
    public const string TextoDemasiadoLargo = "texto_demasiado_largo";
    public const string Conflicto = "conflicto_de_version";
    public const string OrganismoNoExiste = "organismo_no_existe";
    public const string TipoNoExiste = "tipo_no_existe";
    public const string TipoNoElegible = "tipo_no_elegible";
    public const string OrganismoNoOperable = "organismo_no_operable";
}

/// <summary>Rangos permitidos de los parámetros (los mismos CHECK del DDL 164) y validación de los textos de soporte.</summary>
public static partial class ParametrosPnReglas
{
    public const int SesionMinMinutos = 15;
    public const int SesionMaxMinutos = 1440;
    public const int BorradorMinDias = 1;
    public const int BorradorMaxDias = 90;
    public const int CiudadanoMin = 1;
    public const int CiudadanoMax = 100;
    public const int GlobalMin = 1;
    public const int GlobalMax = 100_000;
    public const int CorreoMax = 254;
    public const int TelefonoMax = 30;
    public const int HorarioMax = 120;

    [GeneratedRegex(@"^[0-9+()\- ]{7,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex TelefonoFormato();

    /// <summary>Devuelve el primer error (código, campo, mensaje con el rango) o <c>null</c> si todo es válido.</summary>
    public static (string Error, string Campo, string Mensaje)? Validar(GuardarParametrosPnCommand c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (Fuera(c.SesionVigenciaMinutos, SesionMinMinutos, SesionMaxMinutos))
            return Rango("sesionVigenciaMinutos", "La vigencia de la sesión", SesionMinMinutos, SesionMaxMinutos, "minutos");
        if (Fuera(c.BorradorVigenciaDias, BorradorMinDias, BorradorMaxDias))
            return Rango("borradorVigenciaDias", "La vigencia del borrador", BorradorMinDias, BorradorMaxDias, "días");
        if (Fuera(c.TopeConsultasCiudadanoDia, CiudadanoMin, CiudadanoMax))
            return Rango("topeConsultasCiudadanoDia", "El tope de consultas por ciudadano al día", CiudadanoMin, CiudadanoMax, "consultas");
        if (Fuera(c.TopeConsultasGlobalDia, GlobalMin, GlobalMax))
            return Rango("topeConsultasGlobalDia", "El tope global de consultas al día", GlobalMin, GlobalMax, "consultas");

        if (c.SoporteCorreo is { } correo
            && (correo.Length > CorreoMax || !MailAddress.TryCreate(correo, out var m) || !string.Equals(m.Address, correo, StringComparison.Ordinal)))
            return (ConfiguracionPnErrores.CorreoInvalido, "soporteCorreo", "El correo de soporte no tiene un formato válido.");
        if (c.SoporteTelefono is { } tel && !TelefonoFormato().IsMatch(tel))
            return (ConfiguracionPnErrores.TelefonoInvalido, "soporteTelefono", "El teléfono de soporte admite dígitos, +, paréntesis, guiones y espacios (7 a 30 caracteres).");
        if (c.SoporteHorario is { } horario && horario.Length > HorarioMax)
            return (ConfiguracionPnErrores.TextoDemasiadoLargo, "soporteHorario", $"El horario de soporte admite hasta {HorarioMax} caracteres.");
        return null;
    }

    private static bool Fuera(int? valor, int min, int max) => valor is { } v && (v < min || v > max);

    private static (string, string, string) Rango(string campo, string nombre, int min, int max, string unidad) =>
        (ConfiguracionPnErrores.ValorFueraDeRango, campo, $"{nombre} debe estar entre {min} y {max} {unidad}.");

    /// <summary>Texto opcional: sin espacios sobrantes y nulo si queda vacío.</summary>
    internal static string? Limpiar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}

/// <summary>Parámetros a guardar (nulo = predeterminado del BFF) con la versión que el editor leyó.</summary>
public sealed record GuardarParametrosPnCommand(
    int? SesionVigenciaMinutos,
    int? BorradorVigenciaDias,
    int? TopeConsultasCiudadanoDia,
    int? TopeConsultasGlobalDia,
    bool? CaptchaActivo,
    string? SoporteCorreo,
    string? SoporteTelefono,
    string? SoporteHorario,
    long RowVersion,
    Guid Actor);

public sealed record GuardarParametrosPnResult(ParametrosPn? Parametros, string? Error, string? Campo, string? Mensaje);

/// <summary>HU #14072 AC3/AC4/AC6 — lectura y guardado de los parámetros generales, con rangos, concurrencia y auditoría.</summary>
public sealed class ParametrosPnHandler(IPersonaNaturalConfigRepository config, IAdminAuditWriter audit)
{
    public Task<ParametrosPn> ObtenerAsync(CancellationToken ct = default) => config.ObtenerParametrosAsync(ct);

    public async Task<GuardarParametrosPnResult> GuardarAsync(GuardarParametrosPnCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalizado = command with
        {
            SoporteCorreo = ParametrosPnReglas.Limpiar(command.SoporteCorreo),
            SoporteTelefono = ParametrosPnReglas.Limpiar(command.SoporteTelefono),
            SoporteHorario = ParametrosPnReglas.Limpiar(command.SoporteHorario),
        };
        if (ParametrosPnReglas.Validar(normalizado) is { } invalido)
            return new GuardarParametrosPnResult(null, invalido.Error, invalido.Campo, invalido.Mensaje);

        var anterior = await config.ObtenerParametrosAsync(ct).ConfigureAwait(false);
        var resultado = await config.GuardarParametrosAsync(
            new ParametrosPn(
                normalizado.SesionVigenciaMinutos, normalizado.BorradorVigenciaDias, normalizado.TopeConsultasCiudadanoDia,
                normalizado.TopeConsultasGlobalDia, normalizado.CaptchaActivo, normalizado.SoporteCorreo, normalizado.SoporteTelefono,
                normalizado.SoporteHorario, 0, null, null),
            command.RowVersion, command.Actor, ct).ConfigureAwait(false);
        if (resultado.Conflicto || resultado.Parametros is null)
            return new GuardarParametrosPnResult(
                resultado.Parametros, ConfiguracionPnErrores.Conflicto, null,
                "Otra persona cambió esta configuración mientras la editaba. Recargue para ver su cambio antes de guardar el suyo.");

        await PersonaNaturalAuditoria.EscribirAsync(
            audit, "persona_natural_settings", AuditVocabulary.Operations.Update, command.Actor, null,
            Resumen(anterior), Resumen(resultado.Parametros), ct).ConfigureAwait(false);
        return new GuardarParametrosPnResult(resultado.Parametros, null, null, null);
    }

    private static string Resumen(ParametrosPn p) => PersonaNaturalAuditoria.Json(
        ("sesionVigenciaMinutos", p.SesionVigenciaMinutos), ("borradorVigenciaDias", p.BorradorVigenciaDias),
        ("topeConsultasCiudadanoDia", p.TopeConsultasCiudadanoDia), ("topeConsultasGlobalDia", p.TopeConsultasGlobalDia),
        ("captchaActivo", p.CaptchaActivo), ("soporteCorreo", p.SoporteCorreo), ("soporteTelefono", p.SoporteTelefono),
        ("soporteHorario", p.SoporteHorario));
}

/// <summary>HU #14072 AC1 — qué organismos ofrece el portal: el grant que ya existe de la empresa Persona Natural, con auditoría.</summary>
public sealed class OrganismosPnHandler(IPersonaNaturalConfigRepository config, IAdminAuditWriter audit)
{
    public Task<IReadOnlyList<OrganismoPnView>> ListarAsync(bool soloHabilitados, CancellationToken ct = default) =>
        config.ListarOrganismosAsync(soloHabilitados, ct);

    /// <returns>Código de error (<see cref="ConfiguracionPnErrores"/>) o <c>null</c> si quedó aplicado. Habilitar exige que el
    /// organismo esté operable en FLIT; apagarlo siempre se puede.</returns>
    public async Task<string?> EstablecerAsync(Guid organismoId, bool habilitado, Guid actor, CancellationToken ct = default)
    {
        if (habilitado && !await config.OrganismoEsOperableAsync(organismoId, ct).ConfigureAwait(false))
            return await config.ExisteOrganismoAsync(organismoId, ct).ConfigureAwait(false)
                ? ConfiguracionPnErrores.OrganismoNoOperable
                : ConfiguracionPnErrores.OrganismoNoExiste;

        var cambio = await config.EstablecerOrganismoAsync(organismoId, habilitado, actor, ct).ConfigureAwait(false);
        if (!cambio.Existe)
            return ConfiguracionPnErrores.OrganismoNoExiste;
        if (cambio.EstadoAnterior != habilitado)
            await PersonaNaturalAuditoria.EscribirAsync(
                audit, "persona_natural_organismos", habilitado ? AuditVocabulary.Operations.Activate : AuditVocabulary.Operations.Deactivate,
                actor, organismoId, PersonaNaturalAuditoria.Json(("habilitado", cambio.EstadoAnterior)),
                PersonaNaturalAuditoria.Json(("habilitado", habilitado)), ct).ConfigureAwait(false);
        return null;
    }
}

/// <summary>
/// HU #14216 — interruptor del canal del punto. Encender exige el mismo «operable en FLIT» que habilitar el organismo
/// para el portal. Apagar siempre se puede. No modifica el grant <c>tenant_transit_office_grants</c>.
/// </summary>
public sealed class CanalPuntoPnHandler(IPersonaNaturalConfigRepository config, IAdminAuditWriter audit)
{
    /// <returns>Código de error (<see cref="ConfiguracionPnErrores"/>) o <c>null</c> si quedó aplicado.</returns>
    public async Task<string?> EstablecerAsync(Guid organismoId, bool encendido, Guid actor, CancellationToken ct = default)
    {
        if (encendido && !await config.OrganismoEsOperableAsync(organismoId, ct).ConfigureAwait(false))
            return await config.ExisteOrganismoAsync(organismoId, ct).ConfigureAwait(false)
                ? ConfiguracionPnErrores.OrganismoNoOperable
                : ConfiguracionPnErrores.OrganismoNoExiste;

        var cambio = await config.EstablecerCanalPuntoAsync(organismoId, encendido, actor, ct).ConfigureAwait(false);
        if (!cambio.Existe)
            return ConfiguracionPnErrores.OrganismoNoExiste;
        if (cambio.EstadoAnterior != encendido)
            await PersonaNaturalAuditoria.EscribirAsync(
                audit, "persona_natural_point_channel", encendido ? AuditVocabulary.Operations.Activate : AuditVocabulary.Operations.Deactivate,
                actor, organismoId, PersonaNaturalAuditoria.Json(("encendido", cambio.EstadoAnterior)),
                PersonaNaturalAuditoria.Json(("encendido", encendido)), ct).ConfigureAwait(false);
        return null;
    }
}

/// <summary>
/// HU #14072 AC1 — lista blanca de tipos: «habilitado = elegible (K2) + habilitado aquí + tarifa vigente». Solo se pueden
/// habilitar los candidatos (elegibles y con el wizard encendido); apagar un tipo siempre se puede.
/// </summary>
public sealed class TiposPnHandler(IPersonaNaturalConfigRepository config, IAdminAuditWriter audit)
{
    public Task<IReadOnlyList<TipoPnView>> ListarAsync(CancellationToken ct = default) => config.ListarTiposAsync(ct);

    /// <returns>Código de error (<see cref="ConfiguracionPnErrores"/>) o <c>null</c> si quedó aplicado.</returns>
    public async Task<string?> EstablecerAsync(Guid tipoId, bool habilitado, Guid actor, CancellationToken ct = default)
    {
        if (habilitado && !await config.TipoEsCandidatoAsync(tipoId, ct).ConfigureAwait(false))
            return ConfiguracionPnErrores.TipoNoElegible;

        var cambio = await config.EstablecerTipoAsync(tipoId, habilitado, actor, ct).ConfigureAwait(false);
        if (!cambio.Existe)
            return ConfiguracionPnErrores.TipoNoExiste;
        if (cambio.EstadoAnterior != habilitado)
            await PersonaNaturalAuditoria.EscribirAsync(
                audit, "persona_natural_enabled_types", habilitado ? AuditVocabulary.Operations.Activate : AuditVocabulary.Operations.Deactivate,
                actor, tipoId, PersonaNaturalAuditoria.Json(("habilitado", cambio.EstadoAnterior)),
                PersonaNaturalAuditoria.Json(("habilitado", habilitado)), ct).ConfigureAwait(false);
        return null;
    }
}
