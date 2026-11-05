using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Vehiculos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13730 (F03-H4), AC3 y AC4 (F03-C5, R-3) — precedencia del documento del locatario: el que trae el proveedor
/// (Intempo) reemplaza al declarado por el cliente con origen PROVEEDOR; sin proveedor se conserva el declarado
/// (DECLARADO_CLIENTE); sin ninguno, el vehículo queda sin documento de locatario.
/// </summary>
/// <example>
/// <code>
/// var decision = ResolucionLocatario.Resolver(OrigenDocumento.DeclaradoCliente, proveedorLoTrae: true);
/// // decision.Reemplazar == true; decision.Origen == OrigenDocumento.Proveedor
/// </code>
/// </example>
public sealed class ResolucionLocatarioTests
{
    // AC3 — el proveedor prevalece sobre el declarado.
    [Fact]
    public void Proveedor_prevalece_sobre_declarado()
    {
        ResolucionLocatario.Resolver(OrigenDocumento.DeclaradoCliente, proveedorLoTrae: true)
            .Should().Be(new DecisionLocatario(Reemplazar: true, OrigenDocumento.Proveedor));
        ResolucionLocatario.Resolver(null, proveedorLoTrae: true)
            .Should().Be(new DecisionLocatario(Reemplazar: true, OrigenDocumento.Proveedor), "sin declarado también se autollena");
    }

    // AC4 — sin locatario del proveedor se conserva el declarado.
    [Fact]
    public void Sin_proveedor_conserva_declarado()
    {
        ResolucionLocatario.Resolver(OrigenDocumento.DeclaradoCliente, proveedorLoTrae: false)
            .Should().Be(new DecisionLocatario(Reemplazar: false, OrigenDocumento.DeclaradoCliente));
    }

    // Borde — ni declarado ni proveedor: sin documento de locatario.
    [Fact]
    public void Sin_ninguno_queda_vacio()
    {
        ResolucionLocatario.Resolver(null, proveedorLoTrae: false)
            .Should().Be(new DecisionLocatario(Reemplazar: false, null));
    }
    // HU #13730b (decisión «Guardar el respaldo») — el declarado se respalda solo cuando el proveedor lo reemplaza.
    [Fact]
    public void Declarado_se_respalda_solo_cuando_el_proveedor_lo_reemplaza()
    {
        ResolucionLocatario.RespaldarDeclarado(OrigenDocumento.DeclaradoCliente, proveedorLoTrae: true).Should().BeTrue();
        ResolucionLocatario.RespaldarDeclarado(OrigenDocumento.DeclaradoCliente, proveedorLoTrae: false)
            .Should().BeFalse("sin proveedor el declarado sigue vigente: no hay nada que respaldar");
        ResolucionLocatario.RespaldarDeclarado(null, proveedorLoTrae: true).Should().BeFalse("sin declarado no hay respaldo");
        ResolucionLocatario.RespaldarDeclarado(OrigenDocumento.Proveedor, proveedorLoTrae: true)
            .Should().BeFalse("una segunda consulta al RUNT no pisa el respaldo con el documento del proveedor");
    }
}
