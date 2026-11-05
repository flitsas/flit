using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Diagnostico.Tests.Aislamiento;

/// <summary>
/// HU #13741 (F00.2-H2), condición de ADR-0073 para la vía de escritura del backoffice — prueba de arquitectura (grep
/// auditable, como <see cref="IgnoreQueryFiltersSoloEnBackofficeTests"/>) y de comportamiento: la escritura sobre la fila de
/// OTRA empresa solo se abre con <c>DiagnosticoDb.AutorizarEscrituraDe(empresa, tipos)</c>, que solo se llama desde
/// <c>Modulos/Backoffice/</c>; y ese ámbito acepta una sola empresa, solo los tipos indicados y nunca un borrado.
/// <c>TenantFilter.ValidarEscritura</c> sigue rechazando todo lo demás.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EscrituraEntreEmpresasSoloEnBackofficeTests
/// </code>
/// </example>
public sealed partial class EscrituraEntreEmpresasSoloEnBackofficeTests
{
    private const string ModuloPermitido = "Modulos/Backoffice/";

    // ADR-0073 — la escritura entre empresas solo se abre desde el módulo Backoffice.
    [Fact]
    public void Solo_el_modulo_Backoffice_abre_la_escritura_entre_empresas()
    {
        var src = Path.Combine(IgnoreQueryFiltersSoloEnBackofficeTests.RaizDelServicio(), "src");

        var usos = IgnoreQueryFiltersSoloEnBackofficeTests.Usos(src, Llamada());

        usos.Should().NotBeEmpty("el contrato del backoffice escribe en otra compañía con AutorizarEscrituraDe(): si no hay usos, el escáner no mira donde debe");
        usos.Where(u => !u.Archivo.Contains(ModuloPermitido, StringComparison.Ordinal)).Select(u => $"{u.Archivo}:{u.Linea}")
            .Should().BeEmpty("la escritura entre empresas solo se permite en el módulo Backoffice (ADR-0073)");
    }

    // El escáner detecta una llamada fuera del módulo, acepta la del módulo y no cuenta la declaración ni los comentarios.
    [Fact]
    public void El_escaner_detecta_una_escritura_entre_empresas_fuera_del_modulo_Backoffice()
    {
        var raiz = Directory.CreateTempSubdirectory("dgn-arq-esc-").FullName;
        try
        {
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "Modulos/Diagnosticos/Editar.cs", "using (db\n    .AutorizarEscrituraDe (otra, typeof(T)))");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "Modulos/Backoffice/Contrato.cs", "using var ambito = db.AutorizarEscrituraDe(empresa, tipos);");
            IgnoreQueryFiltersSoloEnBackofficeTests.Escribir(raiz, "Persistence/Db.cs", "internal IDisposable AutorizarEscrituraDe(Guid empresa, params Type[] tipos)\n// db.AutorizarEscrituraDe(x);");

            var usos = IgnoreQueryFiltersSoloEnBackofficeTests.Usos(raiz, Llamada());

            usos.Select(u => $"{u.Archivo}:{u.Linea}").Should().BeEquivalentTo(["Modulos/Diagnosticos/Editar.cs:2", "Modulos/Backoffice/Contrato.cs:1"]);
        }
        finally
        {
            Directory.Delete(raiz, recursive: true);
        }
    }

    // El ámbito acepta solo la empresa y los tipos autorizados, nunca un borrado; sin ámbito todo sigue rechazado.
    [Fact]
    public void Ambito_acepta_solo_la_empresa_y_los_tipos_autorizados()
    {
        var flit = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var autorizada = new EscrituraAutorizada(b, new HashSet<Type> { typeof(ConfiguracionEmpresa), typeof(CambioContrato) });

        using (var db = Contexto(flit))
        {
            db.Add(new ConfiguracionEmpresa { TenantId = b });
            db.Add(new CambioContrato { TenantId = b, Campo = "limite_vehiculos_lote", ValorNuevo = "200" });
            db.Add(new LecturaBackoffice { EmpresaConsultada = b, UsuarioId = Guid.NewGuid(), Ruta = "/propia" });

            var sinAmbito = () => TenantFilter.ValidarEscritura(db.ChangeTracker, flit);
            sinAmbito.Should().Throw<EscrituraEntreEmpresasException>("sin ámbito explícito, otra empresa sigue rechazada");
            var conAmbito = () => TenantFilter.ValidarEscritura(db.ChangeTracker, flit, autorizada);
            conAmbito.Should().NotThrow("la empresa y los tipos autorizados, más la fila propia");
            var otraEmpresa = () => TenantFilter.ValidarEscritura(db.ChangeTracker, flit, autorizada with { Empresa = c });
            otraEmpresa.Should().Throw<EscrituraEntreEmpresasException>("el ámbito es de una sola empresa");
        }

        using (var db = Contexto(flit))
        {
            db.Add(new LecturaBackoffice { TenantId = b, EmpresaConsultada = b, UsuarioId = Guid.NewGuid(), Ruta = "/ajena" });
            var tipoNoAutorizado = () => TenantFilter.ValidarEscritura(db.ChangeTracker, flit, autorizada);
            tipoNoAutorizado.Should().Throw<EscrituraEntreEmpresasException>("LecturaBackoffice no está en el ámbito");
        }

        using (var db = Contexto(flit))
        {
            db.Entry(new ConfiguracionEmpresa { TenantId = b }).State = EntityState.Deleted;
            var borrado = () => TenantFilter.ValidarEscritura(db.ChangeTracker, flit, autorizada);
            borrado.Should().Throw<EscrituraEntreEmpresasException>("el ámbito nunca borra filas de otra empresa");
        }

        using (var db = Contexto(flit))
        {
            // CambioContrato: su TenantId no es la clave (en ConfiguracionEmpresa EF no deja cambiarlo).
            var mover = new CambioContrato { TenantId = c, Campo = "limite_vehiculos_lote", ValorNuevo = "200" };
            db.Attach(mover);
            mover.TenantId = b;
            var cambiarDeEmpresa = () => TenantFilter.ValidarEscritura(db.ChangeTracker, flit, autorizada);
            cambiarDeEmpresa.Should().Throw<EscrituraEntreEmpresasException>("una fila de C no se pasa a B");
        }
    }

    // DiagnosticoDb abre el ámbito solo dentro del using, sin anidar.
    [Fact]
    public void AutorizarEscrituraDe_vale_solo_dentro_del_using_y_no_se_anida()
    {
        var b = Guid.NewGuid();
        using var db = Contexto(Guid.NewGuid());

        using (db.AutorizarEscrituraDe(b, typeof(ConfiguracionEmpresa)))
        {
            db.EscrituraAutorizada.Should().NotBeNull();
            db.EscrituraAutorizada!.Empresa.Should().Be(b);
            db.EscrituraAutorizada.Tipos.Should().BeEquivalentTo([typeof(ConfiguracionEmpresa)]);
            var anidado = () => db.AutorizarEscrituraDe(Guid.NewGuid(), typeof(ConfiguracionEmpresa));
            anidado.Should().Throw<InvalidOperationException>();
        }

        db.EscrituraAutorizada.Should().BeNull("al salir del using se cierra");
        var sinTipos = () => db.AutorizarEscrituraDe(b);
        sinTipos.Should().Throw<ArgumentException>();
        var vacia = () => db.AutorizarEscrituraDe(Guid.Empty, typeof(ConfiguracionEmpresa));
        vacia.Should().Throw<ArgumentException>();
    }

    private static Flit.Diagnostico.Api.Persistence.DiagnosticoDb Contexto(Guid empresa) => DosEmpresas.Contexto(ApiDePrueba.BaseInalcanzable, empresa);

    [GeneratedRegex(@"\.\s*AutorizarEscrituraDe\s*\(")]
    private static partial Regex Llamada();
}
