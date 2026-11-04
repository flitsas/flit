using System.Globalization;
using System.Text.Json.Nodes;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>Cuerpo del POST. Siempre <c>processStep</c> 3, sin <c>credentials</c> y sin cabecera de autenticación.</summary>
internal static class SolicitudServicioImpuesto
{
    public static string Crear(
        string placa,
        string tipoHomologado,
        string numero,
        string departamento,
        string? organismo,
        string? municipio,
        string? departamentoNombre,
        int? modelo,
        string? propietario,
        ContactoImpuesto contacto)
    {
        var detalle = new JsonObject
        {
            ["secretaryDepartmentCode"] = departamento,
            ["secretaryDepartmentDescription"] = departamentoNombre ?? "",
            ["secretaryName"] = organismo ?? "",
            ["secretaryCity"] = municipio ?? "",
            ["vehicleModel"] = modelo?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["vehicleServiceType"] = "",
            ["vehicleFuelType"] = "",
            ["vehicleType"] = "",
            ["OwnerName"] = propietario ?? "",
            ["OwnerCellPhone"] = contacto.Celular,
            ["OwnerPhone"] = contacto.Telefono,
            ["OwnerEmail"] = contacto.Correo,
            ["OwnerAddressDepartment"] = contacto.Departamento,
            ["OwnerAddressCity"] = contacto.Ciudad,
            ["OwnerAddressTypeRoad"] = contacto.TipoVia,
            ["OwnerAddressNumber1"] = contacto.Numero1,
            ["OwnerAddressLetter1"] = contacto.Letra1,
            ["OwnerAddressBis1"] = contacto.Bis1,
            ["OwnerAddressSuffixletter1"] = contacto.Sufijo1,
            ["OwnerAddressCardinality1"] = contacto.Cardinalidad1,
            ["OwnerAddressNumber2"] = contacto.Numero2,
            ["OwnerAddressLetter2"] = contacto.Letra2,
            ["OwnerAddressBis2"] = contacto.Bis2,
            ["OwnerAddressSuffixletter2"] = contacto.Sufijo2,
            ["OwnerAddressPlate"] = contacto.Placa,
            ["OwnerAddressCardinality2"] = contacto.Cardinalidad2,
            ["ownerAddressComplete"] = contacto.DireccionCompleta,
        };
        var raiz = new JsonObject
        {
            ["documentType"] = tipoHomologado,
            ["documentNumber"] = numero,
            ["plateNumber"] = placa,
            ["processStep"] = 3,
            ["detail"] = detalle,
        };
        return raiz.ToJsonString();
    }
}
