using Flit.Api.Derivacion;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Derivacion;

/// <summary>
/// ADR-0084 §3 regla 2 — el autor del borrador es el usuario real que lanzó la acción en Diagnóstico: activo, miembro de
/// la empresa de la llamada, sin suspensión vigente, con <c>tramites.create</c> y el Crear de la familia del tipo. Nunca un
/// usuario de servicio (diferencia con ICT). Es la misma regla que la ruta REST <c>POST /tramites/instances</c>, calculada
/// sobre la base y no sobre un JWT.
/// </summary>
/// <remarks>Uso de ejemplo: <c>ReglaAutorDerivacion.Decidir(snapshot, "traspasos.create", exigenciaActiva: true)</c>.</remarks>
public sealed class ReglaAutorDerivacionTests
{
    private const string Traspasos = "traspasos.create";

    private static SnapshotAutor Autor(
        string estado = "active", bool miembro = true, bool suspendido = false, bool superAdmin = false, params string[] permisos) =>
        new(estado, miembro, suspendido, superAdmin, permisos.Length == 0 ? ["tramites.create", Traspasos] : permisos);

    [Fact]
    public void UsuarioActivoMiembroConAmbosPermisos_PuedeCrear() =>
        ReglaAutorDerivacion.Decidir(Autor(), Traspasos, exigenciaActiva: true).Should().BeTrue();

    [Fact]
    public void LosSlugsSeComparanSinDistinguirMayusculas() =>
        ReglaAutorDerivacion.Decidir(Autor(permisos: ["TRAMITES.CREATE", "Traspasos.Create"]), Traspasos, exigenciaActiva: true).Should().BeTrue();

    [Fact]
    public void UsuarioInexistente_NoPuede() =>
        ReglaAutorDerivacion.Decidir(null, Traspasos, exigenciaActiva: true).Should().BeFalse();

    [Theory]
    [InlineData("inactive")]
    [InlineData("pending")]
    [InlineData("")]
    public void UsuarioNoActivo_NoPuede_NiConLaExigenciaApagada(string estado)
    {
        ReglaAutorDerivacion.Decidir(Autor(estado: estado), Traspasos, exigenciaActiva: true).Should().BeFalse();
        ReglaAutorDerivacion.Decidir(Autor(estado: estado), Traspasos, exigenciaActiva: false).Should().BeFalse();
    }

    [Fact]
    public void UsuarioDeOtraCompania_NoPuede_NiSiendoSuperAdmin()
    {
        ReglaAutorDerivacion.Decidir(Autor(miembro: false), Traspasos, exigenciaActiva: true).Should().BeFalse();
        ReglaAutorDerivacion.Decidir(Autor(miembro: false, superAdmin: true), Traspasos, exigenciaActiva: true).Should().BeFalse();
        ReglaAutorDerivacion.Decidir(Autor(miembro: false), Traspasos, exigenciaActiva: false).Should().BeFalse();
    }

    [Fact]
    public void UsuarioSuspendidoEnLaCompania_NoPuede() =>
        ReglaAutorDerivacion.Decidir(Autor(suspendido: true), Traspasos, exigenciaActiva: true).Should().BeFalse();

    [Fact]
    public void SinTramitesCreate_NoPuede() =>
        ReglaAutorDerivacion.Decidir(Autor(permisos: [Traspasos]), Traspasos, exigenciaActiva: true).Should().BeFalse();

    [Fact]
    public void SinElCrearDeLaFamilia_NoPuede() =>
        ReglaAutorDerivacion.Decidir(Autor(permisos: ["tramites.create", "matriculas.create"]), Traspasos, exigenciaActiva: true).Should().BeFalse();

    [Fact]
    public void SuperAdminMiembroDeLaCompania_PuedeSinSlugs_ComoElBypassDeLaRutaRest() =>
        ReglaAutorDerivacion.Decidir(Autor(superAdmin: true, permisos: ["nada"]), Traspasos, exigenciaActiva: true).Should().BeTrue();

    [Fact]
    public void ConLaExigenciaDePermisosApagada_BastaSerMiembroActivo_ComoEnLaRutaRest() =>
        ReglaAutorDerivacion.Decidir(Autor(permisos: ["nada"]), Traspasos, exigenciaActiva: false).Should().BeTrue();
}
