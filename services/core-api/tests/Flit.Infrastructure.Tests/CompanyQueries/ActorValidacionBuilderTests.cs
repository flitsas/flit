using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Identity;
using Xunit;

namespace Flit.Infrastructure.Tests.CompanyQueries;

public sealed class ActorValidacionBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<Guid, int> SinConteos = new Dictionary<Guid, int>();
    private static readonly IReadOnlySet<Guid> SinAuditoria = new HashSet<Guid>();

    private static ProcedureInstanceActor Actor(string tipo, int ordinal, string doc, string nombre = "Persona",
        string? personType = null, string? rlDoc = null) => new()
    {
        ActorType = tipo, Ordinal = ordinal, DocumentType = "CC", DocumentNumber = doc, FullName = nombre,
        PersonType = personType,
        Metadata = rlDoc is null
            ? "{}"
            : JsonSerializer.Serialize(new
            {
                representanteLegal = new { tipoDocumento = "CC", numeroDocumento = rlDoc, nombreCompleto = "RL" },
            }),
    };

    private static ProcedureInstanceBiometricValidation Val(string rol, string doc, string estado, int dias = 0,
        int attempts = 0, string? detail = null) => new()
    {
        Id = Guid.NewGuid(), PartyRole = rol, DocumentType = "CC", DocumentNumber = doc, Status = estado,
        Attempts = attempts, Detail = detail, CreatedAt = T0.AddDays(dias), UpdatedAt = T0.AddDays(dias),
    };

    private static IReadOnlyList<ActorValidacionDto> Build(string familia, ProcedureInstanceActor[] a,
        ProcedureInstanceBiometricValidation[] v, IReadOnlyDictionary<Guid, int>? conteos = null,
        IReadOnlySet<Guid>? conAud = null) =>
        ActorValidacionBuilder.Build(familia, a, v, conteos ?? SinConteos, conAud ?? SinAuditoria);

    [Fact]
    public void Traspaso_uno_mas_uno_sin_validar()
    {
        var r = Build(ProcedureFamilyCodes.Traspaso,
            [Actor("comprador", 1, "1"), Actor("vendedor", 1, "2")], []);

        r.Select(x => (x.Rol, x.EstadoValidacion, x.Intentos)).Should().Equal(
            ("comprador", "sin_validacion", 0), ("vendedor", "sin_validacion", 0));
    }

    [Theory]
    [InlineData(ProcedureFamilyCodes.Matriculas)]
    [InlineData(ProcedureFamilyCodes.Otros)]
    public void Fuera_de_traspaso_el_comprador_guardado_sale_como_propietario(string familia)
    {
        var r = Build(familia, [Actor("comprador", 1, "1")], []);
        r.Should().ContainSingle().Which.Rol.Should().Be("propietario");
    }

    [Fact]
    public void Locatario_no_se_emite()
    {
        Build(ProcedureFamilyCodes.Otros, [Actor("locatario", 1, "9"), Actor("comprador", 1, "1")], [])
            .Should().ContainSingle();
    }

    [Fact]
    public void Dos_vendedores_uno_rechazado_luego_aprobado_conserva_motivo_e_intentos_por_persona()
    {
        var rech = Val("vendedor", "2", BiometricEstados.Rechazado, dias: 0,
            detail: "{\"motivo\":\"Coincidencia facial insuficiente\"}");
        var apro = Val("vendedor", "2", BiometricEstados.Aprobado, dias: 1);
        var otro = Val("vendedor", "3", BiometricEstados.Aprobado, dias: 0);
        var conteos = new Dictionary<Guid, int> { [rech.Id] = 2, [apro.Id] = 1, [otro.Id] = 1 };
        var conAud = new HashSet<Guid> { rech.Id, apro.Id, otro.Id };

        var r = Build(ProcedureFamilyCodes.Traspaso,
            [Actor("comprador", 1, "1"), Actor("vendedor", 1, "2"), Actor("vendedor", 2, "3")],
            [rech, apro, otro], conteos, conAud);

        var v1 = r.Single(x => x.Rol == "vendedor" && x.Ordinal == 1);
        v1.EstadoValidacion.Should().Be("aprobado");
        v1.MotivoRechazo.Should().Be("El rostro no coincide con el documento.");
        v1.Intentos.Should().Be(3);
        var v2 = r.Single(x => x.Rol == "vendedor" && x.Ordinal == 2);
        v2.MotivoRechazo.Should().BeNull();
        v2.Intentos.Should().Be(1);
    }

    [Fact]
    public void Sin_auditoria_cuenta_attempts_de_la_fila()
    {
        var v = Val("comprador", "1", BiometricEstados.Rechazado, attempts: 2);
        Build(ProcedureFamilyCodes.Traspaso, [Actor("comprador", 1, "1")], [v])
            .Single().Intentos.Should().Be(2);
    }

    [Fact]
    public void Con_auditoria_manda_la_auditoria_aunque_attempts_se_reinicie()
    {
        var v = Val("comprador", "1", BiometricEstados.Aprobado, attempts: 0);
        Build(ProcedureFamilyCodes.Traspaso, [Actor("comprador", 1, "1")], [v],
                new Dictionary<Guid, int> { [v.Id] = 4 }, new HashSet<Guid> { v.Id })
            .Single().Intentos.Should().Be(4);
    }

    [Fact]
    public void Juridica_con_un_actor_enlaza_por_documento_del_representante_legal()
    {
        var a = Actor("comprador", 1, "900123", "Empresa SAS", personType: "juridical", rlDoc: "555");
        var v = Val("comprador", "555", BiometricEstados.Aprobado);
        Build(ProcedureFamilyCodes.Traspaso, [a], [v]).Single().EstadoValidacion.Should().Be("aprobado");
    }

    [Fact]
    public void Un_solo_actor_sin_coincidencia_de_documento_toma_las_validaciones_del_rol()
    {
        var a = Actor("comprador", 1, "900123", "Empresa SAS", personType: "juridical");
        var v = Val("comprador", "777", BiometricEstados.Enviado);
        Build(ProcedureFamilyCodes.Traspaso, [a], [v]).Single().EstadoValidacion.Should().Be("enviado");
    }

    [Fact]
    public void Dos_actores_sin_coincidencia_salen_sin_validar_y_no_heredan_la_validacion_de_otro()
    {
        var a1 = Actor("comprador", 1, "900123", "Empresa A", personType: "juridical");
        var a2 = Actor("comprador", 2, "900456", "Empresa B", personType: "juridical");
        var v = Val("comprador", "777", BiometricEstados.Aprobado, attempts: 3);
        var r = Build(ProcedureFamilyCodes.Traspaso, [a1, a2], [v]);
        r.Should().OnlyContain(x => x.EstadoValidacion == "sin_validacion" && x.Intentos == 0);
    }

    [Fact]
    public void Una_validacion_de_vendedor_no_se_atribuye_al_comprador_con_el_mismo_documento()
    {
        var v = Val("vendedor", "1", BiometricEstados.Aprobado);
        Build(ProcedureFamilyCodes.Traspaso, [Actor("comprador", 1, "1")], [v])
            .Single().EstadoValidacion.Should().Be("sin_validacion");
    }

    [Fact]
    public void Estado_desconocido_sale_sin_validar()
    {
        var v = Val("comprador", "1", "algo_raro");
        Build(ProcedureFamilyCodes.Traspaso, [Actor("comprador", 1, "1")], [v])
            .Single().EstadoValidacion.Should().Be("sin_validacion");
    }

    [Fact]
    public void Ninguna_salida_menciona_el_mecanismo_de_validacion()
    {
        var v = Val("comprador", "1", BiometricEstados.Rechazado, detail: "{\"motivo\":\"Rechazado por Kyverum (manual)\"}");
        var json = JsonSerializer.Serialize(
            Build(ProcedureFamilyCodes.Traspaso, [Actor("comprador", 1, "1")], [v]));
        foreach (var p in IdentityRejectionReasonText.Prohibidas)
            json.Should().NotContainEquivalentOf(p);
    }

    [Fact]
    public void Maximos_cuenta_por_rol_sobre_todas_las_filas()
    {
        var f1 = Build(ProcedureFamilyCodes.Traspaso, [Actor("comprador", 1, "1"), Actor("vendedor", 1, "2")], []);
        var f2 = Build(ProcedureFamilyCodes.Traspaso,
            [Actor("comprador", 1, "1"), Actor("vendedor", 1, "2"), Actor("vendedor", 2, "3")], []);
        var f3 = Build(ProcedureFamilyCodes.Matriculas, [Actor("comprador", 1, "1"), Actor("comprador", 2, "2")], []);

        ActorValidacionBuilder.Maximos([f1, f2, f3]).Should().Be(new MaxActoresPorRolDto(1, 2, 2));
        ActorValidacionBuilder.Maximos([]).Should().Be(new MaxActoresPorRolDto(0, 0, 0));
    }

    [Theory]
    [InlineData("webhook_applied", "rechazado", true)]
    [InlineData("manual_captura_recibida", "received", true)]
    [InlineData("reconcile", "aprobado", true)]
    [InlineData("reconcile", "pending", false)]
    [InlineData("send", "ok", false)]
    [InlineData("webhook_received", "received", false)]
    public void Etapas_que_cuentan_como_intento(string stage, string outcome, bool cuenta) =>
        ActorValidacionBuilder.CuentaComoIntento(stage, outcome).Should().Be(cuenta);
}
