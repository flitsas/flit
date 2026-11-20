using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flit.Consultas.Grpc.V1;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>Cuerpo del POST asíncrono (D3) y normalización/validación D9. Sin <c>processStep</c>.</summary>
internal static class SolicitudServicioImpuesto
{
    public const string ClaveDocumentType = "documentType";
    public const string ClaveDocumentNumber = "documentNumber";
    public const string ClavePlateNumber = "plateNumber";
    public const string ClaveSecretaryDepartmentCode = "secretaryDepartmentCode";
    public const string ClaveSecretaryCity = "secretaryCity";
    public const string ClaveVehicleModel = "vehicleModel";
    public const string ClaveOwnerName = "OwnerName";
    public const string ClaveOwnerAddressCity = "OwnerAddressCity";
    public const string ClaveOwnerAddressDepartment = "OwnerAddressDepartment";
    public const string ClaveOwnerAddressComplete = "OwnerAddressComplete";

    private static readonly Regex PlacaValida = new("^[A-Z0-9]{6}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DocumentoValido = new("^[A-Za-z0-9]{3,20}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DepartamentoCodigo = new("^[0-9]{2}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> TiposDocumento = new(StringComparer.Ordinal) { "CC", "NIT", "CE", "PA", "TI", "CD" };

    public static string Crear(ConsultarImpuestoVehicularRequest request, string tipoHomologado, ContactoImpuesto contacto)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(contacto);

        var telefono = request.HasPropietarioTelefono ? SoloDigitos(request.PropietarioTelefono) : "";
        var esCelular = telefono.Length == 10 && telefono.StartsWith('3');
        var tieneDireccionRunt = request.HasPropietarioDireccion && !string.IsNullOrWhiteSpace(request.PropietarioDireccion);

        var municipio = NormalizarMunicipio(request.HasMunicipio ? request.Municipio : null, out var deptoDesdeCiudad);
        var ownerCity = NormalizarMunicipio(
            request.HasPropietarioCiudad ? request.PropietarioCiudad : null,
            out var deptoDesdeOwner);
        if (string.IsNullOrEmpty(ownerCity))
            ownerCity = NormalizarMunicipio(contacto.Ciudad, out _);

        var ownerDepto = SoloNombre(request.HasPropietarioDepartamento ? request.PropietarioDepartamento : null);
        if (string.IsNullOrEmpty(ownerDepto))
            ownerDepto = SoloNombre(deptoDesdeOwner) is { Length: > 0 } d1 ? d1
                : SoloNombre(deptoDesdeCiudad) is { Length: > 0 } d2 ? d2
                : SoloNombre(contacto.Departamento);

        var deptoNombre = SoloNombre(request.HasDepartamentoNombre ? request.DepartamentoNombre : null);

        var detalle = new JsonObject
        {
            [ClaveSecretaryDepartmentCode] = request.DepartamentoCodigo ?? "",
            ["secretaryDepartmentDescription"] = deptoNombre,
            ["secretaryName"] = request.HasOrganismoNombre ? (request.OrganismoNombre ?? "") : "",
            [ClaveSecretaryCity] = municipio,
            [ClaveVehicleModel] = request.HasModelo
                ? request.Modelo.ToString(CultureInfo.InvariantCulture)
                : "",
            ["vehicleType"] = request.HasVehiculoClase ? (request.VehiculoClase ?? "") : "",
            ["vehicleBrand"] = request.HasVehiculoMarca ? (request.VehiculoMarca ?? "") : "",
            ["vehicleServiceType"] = request.HasVehiculoServicio ? (request.VehiculoServicio ?? "") : "",
            ["vehicleFuelType"] = request.HasVehiculoCombustible ? (request.VehiculoCombustible ?? "") : "",
            [ClaveOwnerName] = request.HasPropietarioNombre ? (request.PropietarioNombre ?? "") : "",
            ["OwnerCellPhone"] = esCelular ? telefono : (string.IsNullOrEmpty(telefono) ? contacto.Celular : ""),
            ["OwnerPhone"] = !esCelular && telefono.Length > 0 ? telefono : (esCelular ? "" : contacto.Telefono),
            ["OwnerEmail"] = request.HasPropietarioCorreo && !string.IsNullOrWhiteSpace(request.PropietarioCorreo)
                ? request.PropietarioCorreo.Trim()
                : contacto.Correo,
            [ClaveOwnerAddressDepartment] = ownerDepto,
            [ClaveOwnerAddressCity] = ownerCity,
        };

        if (tieneDireccionRunt)
        {
            detalle[ClaveOwnerAddressComplete] = request.PropietarioDireccion.Trim();
            detalle["OwnerAddressTypeRoad"] = "";
            detalle["OwnerAddressNumber1"] = "";
            detalle["OwnerAddressLetter1"] = "";
            detalle["OwnerAddressBis1"] = false;
            detalle["OwnerAddressSuffixletter1"] = "";
            detalle["OwnerAddressCardinality1"] = "";
            detalle["OwnerAddressNumber2"] = "";
            detalle["OwnerAddressLetter2"] = "";
            detalle["OwnerAddressBis2"] = false;
            detalle["OwnerAddressSuffixletter2"] = "";
            detalle["OwnerAddressPlate"] = "";
            detalle["OwnerAddressCardinality2"] = "";
        }
        else
        {
            detalle[ClaveOwnerAddressComplete] = contacto.DireccionCompleta;
            detalle["OwnerAddressTypeRoad"] = contacto.TipoVia;
            detalle["OwnerAddressNumber1"] = contacto.Numero1;
            detalle["OwnerAddressLetter1"] = contacto.Letra1;
            detalle["OwnerAddressBis1"] = contacto.Bis1;
            detalle["OwnerAddressSuffixletter1"] = contacto.Sufijo1;
            detalle["OwnerAddressCardinality1"] = contacto.Cardinalidad1;
            detalle["OwnerAddressNumber2"] = contacto.Numero2;
            detalle["OwnerAddressLetter2"] = contacto.Letra2;
            detalle["OwnerAddressBis2"] = contacto.Bis2;
            detalle["OwnerAddressSuffixletter2"] = contacto.Sufijo2;
            detalle["OwnerAddressPlate"] = contacto.Placa;
            detalle["OwnerAddressCardinality2"] = contacto.Cardinalidad2;
        }

        var placa = (request.Placa?.Valor ?? "").Trim().ToUpperInvariant();
        var raiz = new JsonObject
        {
            [ClaveDocumentType] = tipoHomologado,
            [ClaveDocumentNumber] = request.Documento?.Numero?.Trim() ?? "",
            [ClavePlateNumber] = placa,
            ["detail"] = detalle,
        };
        return raiz.ToJsonString();
    }

    /// <summary>D9: null si válido; si no, el nombre del campo que falla (sin valores).</summary>
    public static string? Validar(string cuerpoJson, bool departamentoActivo)
    {
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(cuerpoJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return ClavePlateNumber;
        }

        if (raiz is not JsonObject obj)
            return ClavePlateNumber;

        var placa = obj[ClavePlateNumber]?.GetValue<string>() ?? "";
        if (!PlacaValida.IsMatch(placa))
            return ClavePlateNumber;

        var tipo = obj[ClaveDocumentType]?.GetValue<string>() ?? "";
        if (!TiposDocumento.Contains(tipo))
            return ClaveDocumentType;

        var numero = obj[ClaveDocumentNumber]?.GetValue<string>() ?? "";
        if (!DocumentoValido.IsMatch(numero))
            return ClaveDocumentNumber;

        var detail = obj["detail"] as JsonObject;
        var codigo = detail?[ClaveSecretaryDepartmentCode]?.GetValue<string>() ?? "";
        if (!DepartamentoCodigo.IsMatch(codigo) || !departamentoActivo)
            return ClaveSecretaryDepartmentCode;

        if (string.IsNullOrWhiteSpace(detail?[ClaveVehicleModel]?.GetValue<string>()))
            return ClaveVehicleModel;
        if (string.IsNullOrWhiteSpace(detail?[ClaveSecretaryCity]?.GetValue<string>()))
            return ClaveSecretaryCity;
        if (string.IsNullOrWhiteSpace(detail?[ClaveOwnerName]?.GetValue<string>()))
            return ClaveOwnerName;

        return null;
    }

    /// <summary>Si el valor trae « - », queda lo que sigue al último separador (municipio). La parte izquierda es el departamento inferido.</summary>
    public static string NormalizarMunicipio(string? valor, out string? departamentoInferido)
    {
        departamentoInferido = null;
        if (string.IsNullOrWhiteSpace(valor))
            return "";
        var texto = valor.Trim();
        var idx = texto.LastIndexOf(" - ", StringComparison.Ordinal);
        if (idx < 0)
            return texto.ToUpperInvariant();
        departamentoInferido = texto[..idx].Trim().ToUpperInvariant();
        return texto[(idx + 3)..].Trim().ToUpperInvariant();
    }

    private static string SoloNombre(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return "";
        var texto = valor.Trim();
        var idx = texto.LastIndexOf(" - ", StringComparison.Ordinal);
        var nombre = idx < 0 ? texto : texto[..idx].Trim();
        return nombre.ToUpperInvariant();
    }

    private static string SoloDigitos(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";
        return string.Concat(texto.Where(char.IsDigit));
    }
}
