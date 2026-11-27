using Flit.Impuestos.Api.Modulos.ApiLiquidacion;
using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Lo que se guarda del ítem al armar la solicitud (DA-7): documento y nombre del propietario (se cifran al guardar), departamento y
/// ciudad de la secretaría. Nunca la dirección, el correo ni el teléfono. <see cref="ToString"/> no imprime datos.
/// </summary>
public sealed record DatosItem(
    string? DocumentoTipo, string? DocumentoNumero, string? NombrePropietario, string? DepartamentoCodigo, string? DepartamentoNombre, string? Ciudad)
{
    /// <summary>Máximo de <c>secretaria_nombre</c> (1.2.0).</summary>
    public const int LargoMaximoSecretariaNombre = 120;

    /// <summary>Nombre de la secretaría (el organismo del RUNT, el mismo que va como <c>secretaryName</c>); null si el RUNT no lo trae.</summary>
    public string? SecretariaNombre { get; init; }

    public override string ToString() => $"DatosItem {{ Departamento = {DepartamentoCodigo}, [REDACTADO: PII] }}";
}

/// <summary>Resultado de armar la solicitud de un ítem.</summary>
public abstract record Construccion(DatosItem Datos)
{
    /// <summary>Completa: se envía al API.</summary>
    public sealed record Lista(SolicitudConsulta Solicitud, DatosItem Datos) : Construccion(Datos);

    /// <summary>Falta un dato (<see cref="Campo"/>, nombre del campo del payload o <c>secretaryDepartmentCode</c> sin homologar).</summary>
    public sealed record Incompleta(string Campo, DatosItem Datos) : Construccion(Datos);

    /// <summary>El departamento no está en la configuración contractual de la compañía (o no tiene fuente activa).</summary>
    public sealed record NoHabilitado(DatosItem Datos) : Construccion(Datos);
}

/// <summary>
/// Arma <c>POST /api/v1/consultas</c> (guía §5) para una placa con lo que trajo el RUNT (plan §2.2; F2a-CF4, F2a-CF5, F5a-CF3), en
/// este orden:
/// <list type="number">
///   <item>Documento: tipo del API (si no, «Datos incompletos» <c>documentType</c>) y número de 3 a 20 letras o dígitos (sin
///   puntos ni espacios); con NIT, sin dígito de verificación: lo que va antes del guion, o el número tal cual si tiene de 3 a 9
///   dígitos; con exactamente 10 dígitos sin guion, los 9 primeros si el último es su dígito de verificación DIAN (módulo 11);
///   cualquier otro caso de 10 o más sin guion es ambiguo (<c>documentNumber</c>).</item>
///   <item>Secretaría homologada (<c>secretaryDepartmentCode</c> si no la hay).</item>
///   <item>Departamento habilitado: el de la copia de configuración del lote; sin lista, el que tiene fuente activa en
///   <c>/fuentes</c>. Si no, «Departamento no habilitado».</item>
///   <item><c>detail</c>: siempre <c>secretaryDepartmentCode</c> y <c>secretaryCity</c>; <c>secretaryName</c>,
///   <c>vehicleModel</c> y <c>OwnerName</c> si el RUNT los trae; y los que exija el <c>params_schema</c> del departamento (el
///   contacto y la dirección solo así). Cundinamarca (25) exige siempre correo (<c>OwnerEmail</c>) y celular o teléfono
///   (<c>OwnerPhone</c>): sin contacto de respaldo (DA-9a=d). Un campo exigido que falta deja «Datos incompletos» con ese campo
///   (<c>OwnerCellPhone</c> y el teléfono cuentan como <c>OwnerPhone</c>; las partes de la dirección, como <c>OwnerAddress</c>).</item>
/// </list>
/// Nunca agrega los campos del contrato anterior. El <c>callbackUrl</c> lo decide quien llama (<see cref="ConCallback"/>).
/// </summary>
/// <example>
/// <code>
/// var r = ConstructorSolicitud.Construir(item.Placa, datosRunt, secretaria, copia, fuentes, callbackUrl);
/// if (r is Construccion.Lista lista) { await api.CrearConsultaAsync(apiKey, lista.Solicitud, ct); }
/// </code>
/// </example>
public static class ConstructorSolicitud
{
    /// <summary>Cundinamarca: sin correo y teléfono el portal no genera la factura (guía §4).</summary>
    public const string DepartamentoContacto = "25";

    public const string CampoDocumentoTipo = "documentType";
    public const string CampoDocumentoNumero = "documentNumber";
    public const string CampoSecretaria = "secretaryDepartmentCode";
    public const string CampoCorreo = "OwnerEmail";
    public const string CampoTelefono = "OwnerPhone";
    public const string CampoCelular = "OwnerCellPhone";
    public const string CampoDireccion = "OwnerAddress";

    public static Construccion Construir(
        string placa, DatosRunt runt, SecretariaHomologada? secretaria, CopiaConfiguracion copia, IReadOnlyList<FuenteApi> fuentes, string? callbackUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placa);
        ArgumentNullException.ThrowIfNull(runt);
        ArgumentNullException.ThrowIfNull(copia);
        ArgumentNullException.ThrowIfNull(fuentes);

        var tipo = runt.TipoDocumento;
        var numero = tipo is null ? null : NumeroDocumento(tipo, runt.NumeroDocumento);
        var fuente = secretaria is null ? null : fuentes.FirstOrDefault(f => f.DepartamentoCodigo == secretaria.DepartamentoCodigo);
        var organismo = Texto(runt.OrganismoNombre);
        var datos = new DatosItem(tipo, numero ?? Texto(runt.NumeroDocumento), Texto(runt.NombrePropietario),
            secretaria?.DepartamentoCodigo, fuente?.Nombre, secretaria?.Ciudad)
        {
            SecretariaNombre = organismo is { Length: > DatosItem.LargoMaximoSecretariaNombre } ? organismo[..DatosItem.LargoMaximoSecretariaNombre] : organismo,
        };

        if (tipo is null)
            return new Construccion.Incompleta(CampoDocumentoTipo, datos);
        if (numero is null)
            return new Construccion.Incompleta(CampoDocumentoNumero, datos);
        if (secretaria is null)
            return new Construccion.Incompleta(CampoSecretaria, datos);
        if (!Habilitado(secretaria.DepartamentoCodigo, copia, fuente))
            return new Construccion.NoHabilitado(datos);

        var detalle = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [CampoSecretaria] = secretaria.DepartamentoCodigo,
            ["secretaryCity"] = secretaria.Ciudad,
        };
        Agregar(detalle, "secretaryName", runt.OrganismoNombre);
        Agregar(detalle, "vehicleModel", runt.Modelo);
        Agregar(detalle, "OwnerName", runt.NombrePropietario);

        var celular = EsCelular(runt.Telefono);
        if (secretaria.DepartamentoCodigo == DepartamentoContacto)
        {
            if (Texto(runt.Correo) is null)
                return new Construccion.Incompleta(CampoCorreo, datos);
            if (Texto(runt.Telefono) is null)
                return new Construccion.Incompleta(CampoTelefono, datos);
            Agregar(detalle, CampoCorreo, runt.Correo);
            Agregar(detalle, celular ? CampoCelular : CampoTelefono, runt.Telefono);
        }

        foreach (var requerido in ValidadorParamsSchema.RequeridosDetalle(fuente?.ParamsSchema))
        {
            if (!detalle.ContainsKey(requerido) && Valor(requerido, runt, celular) is { } valor)
                detalle[requerido] = valor;
        }

        var falta = ValidadorParamsSchema.PrimerFaltante(ValidadorParamsSchema.RequeridosDetalle(fuente?.ParamsSchema), detalle);
        if (falta is not null)
            return new Construccion.Incompleta(CampoFaltante(falta), datos);

        foreach (var prohibido in SolicitudConsulta.CamposProhibidos)
            detalle.Remove(prohibido);
        return new Construccion.Lista(new SolicitudConsulta(tipo, numero, placa, callbackUrl, detalle), datos);
    }

    /// <summary>
    /// El envío lleva <c>callbackUrl</c> solo si la compañía está en <c>LISTO</c> (tiene secreto de webhook), no está en modo respaldo
    /// por firmas inválidas, el secreto existe y la URL del receptor está configurada (F2a-CF8, F2b-CF3).
    /// </summary>
    public static bool ConCallback(EstadoAprovisionamiento estado, bool modoRespaldoFirma, bool tieneSecreto, bool urlConfigurada) =>
        estado == EstadoAprovisionamiento.Listo && !modoRespaldoFirma && tieneSecreto && urlConfigurada;

    /// <summary>Número del documento para el API (ver la clase); null si no se puede usar.</summary>
    public static string? NumeroDocumento(string tipo, string? numero)
    {
        ArgumentNullException.ThrowIfNull(tipo);
        if (string.IsNullOrWhiteSpace(numero))
            return null;
        var limpio = new string([.. numero.Where(c => c is not ('.' or ' ' or ','))]).ToUpperInvariant();
        if (tipo == "NIT")
        {
            var guion = limpio.IndexOf('-', StringComparison.Ordinal);
            if (guion >= 0)
                limpio = limpio[..guion];
            else if (limpio.Length == 10 && limpio.All(char.IsAsciiDigit) && limpio[9] - '0' == DigitoVerificacion(limpio[..9]))
                limpio = limpio[..9];
            else if (limpio.Length > 9)
                return null;
        }

        return limpio.Length is >= 3 and <= 20 && limpio.All(char.IsAsciiLetterOrDigit) ? limpio : null;
    }

    private static readonly int[] PesosDv = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>
    /// Dígito de verificación DIAN de un NIT de solo dígitos (hasta 15): pesos 3, 7, 13, … de derecha a izquierda, residuo r de
    /// la suma módulo 11; DV = r si r es 0 o 1, si no 11 − r.
    /// </summary>
    private static int DigitoVerificacion(string nit)
    {
        var suma = 0;
        for (var i = 0; i < nit.Length; i++)
            suma += (nit[nit.Length - 1 - i] - '0') * PesosDv[i];
        var residuo = suma % 11;
        return residuo <= 1 ? residuo : 11 - residuo;
    }

    private static bool Habilitado(string departamento, CopiaConfiguracion copia, FuenteApi? fuente) =>
        copia.DepartamentosHabilitados is null ? fuente is { Activa: true } : copia.DepartamentoHabilitado(departamento);

    /// <summary>Lo que el RUNT puede aportar a un campo de <c>detail</c> (guía §5); null si no lo trae.</summary>
    private static string? Valor(string campo, DatosRunt runt, bool celular) => campo switch
    {
        "vehicleBrand" => Texto(runt.Marca),
        CampoCorreo => Texto(runt.Correo),
        CampoCelular => celular ? Texto(runt.Telefono) : null,
        CampoTelefono => celular ? null : Texto(runt.Telefono),
        "OwnerAddressDepartment" => Texto(runt.DireccionDepartamento),
        "OwnerAddressCity" => Texto(runt.DireccionCiudad),
        "OwnerAddressComplete" => Texto(runt.Direccion),
        _ => null,
    };

    /// <summary>El campo que se muestra como faltante (enum <c>CampoFaltante</c> del contrato).</summary>
    private static string CampoFaltante(string campo) => campo switch
    {
        CampoCelular => CampoTelefono,
        _ when campo.StartsWith(CampoDireccion, StringComparison.Ordinal) => CampoDireccion,
        _ => campo,
    };

    /// <summary>Un celular colombiano: 10 dígitos que empiezan por 3.</summary>
    private static bool EsCelular(string? telefono) =>
        Texto(telefono) is { Length: 10 } t && t[0] == '3' && t.All(char.IsAsciiDigit);

    private static void Agregar(Dictionary<string, object?> detalle, string campo, string? valor)
    {
        if (Texto(valor) is { } t)
            detalle[campo] = t;
    }

    private static string? Texto(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
