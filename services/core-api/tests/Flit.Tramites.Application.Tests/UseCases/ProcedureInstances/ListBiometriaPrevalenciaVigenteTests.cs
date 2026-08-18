using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #11615 — el resumen del trámite no apalancaba la identidad VIGENTE referenciada de otro trámite.
///
/// <para>El listado <c>GET /instances/{id}/biometric</c> es la única fuente del paso de Identidad, del
/// expediente y del resumen del paso FUR, pero cada consumidor elige "la validación de la parte" con una
/// heurística posicional distinta (primera vs. última coincidencia del rol). Mientras la identidad
/// referenciada se agregaba AL FINAL, cualquier intento local rechazado / expirado / en vuelo la
/// suplantaba en el resumen: pedía validar de nuevo y el botón respondía "ya la tienes activa".</para>
///
/// <para>Uso de ejemplo:
/// <code>
/// var handler = new ListBiometriaHandler(repo, new BiometricsProviderOptions());
/// var (result, _) = await handler.HandleAsync(instanceId, tenantId, ct);
/// var comprador = result!.Validations.First(v => v.PartyRole == "comprador"); // aprobada, no el intento
/// </code>
/// </para>
/// </summary>
public sealed class ListBiometriaPrevalenciaVigenteTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Ayer = DateTimeOffset.UtcNow.AddDays(-1);

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static ProcedureInstanceActor ActorNatural(string parte, string documento) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ActorType = parte,
            DocumentType = "CC",
            DocumentNumber = documento,
            FullName = $"PERSONA {parte.ToUpperInvariant()}",
            PersonType = "natural",
        };

    private static ProcedureInstance Instancia(Guid id, string modalidad) =>
        new()
        {
            Id = id,
            TenantId = TenantId,
            ProcedureTypeId = Guid.NewGuid(),
            ReferenceNumber = "TRM-2026-000042",
            ModalidadEntrada = modalidad,
            CreatedAt = Ayer,
        };

    private static ProcedureInstanceBiometricValidation Validacion(
        string? parte,
        string documento,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? validatedAt = null,
        DateTimeOffset? validUntil = null,
        Guid? procedureInstanceId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProcedureInstanceId = procedureInstanceId,
            PartyRole = parte,
            Name = "PERSONA DEMO",
            DocumentType = "CC",
            DocumentNumber = documento,
            Email = "demo@x.com",
            Status = status,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = createdAt.AddHours(24),
            CreatedAt = createdAt,
            ValidatedAt = validatedAt,
            ValidUntil = validUntil,
        };

    /// <summary>Identidad APROBADA Y VIGENTE de otro trámite (la que se referencia sin clonar).</summary>
    private static ProcedureInstanceBiometricValidation VidVigenteDeOtroTramite(string documento, string? rolOrigen = "vendedor") =>
        Validacion(rolOrigen, documento, BiometricEstados.Aprobado, Ayer.AddDays(-10),
            validatedAt: Ayer.AddDays(-10), validUntil: DateTimeOffset.UtcNow.AddDays(20),
            procedureInstanceId: Guid.NewGuid());

    private static (ListBiometriaHandler Handler, Guid Id) Handler(
        ProcedureInstance instance,
        params (string Documento, ProcedureInstanceBiometricValidation? Vigente)[] referencias)
    {
        var repo = Substitute.For<IProcedureInstanceRepository>();
        repo.GetByIdWithBiometricsAndActorsAsync(instance.Id, TenantId, Arg.Any<CancellationToken>())
            .Returns(instance);
        foreach (var (documento, vigente) in referencias)
        {
            repo.FindVigenteApprovedByDocumentAsync(
                    TenantId, "CC", documento, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(vigente);
        }

        return (new ListBiometriaHandler(repo, new BiometricsProviderOptions()), instance.Id);
    }

    /// <summary>
    /// Selección del RESUMEN del paso FUR: la PRIMERA coincidencia del rol (con el respaldo de las filas
    /// sin rol, como en matrícula).
    /// </summary>
    private static BiometricValidationDto? PrimeraDelRol(BiometricValidationsResponse r, string parte) =>
        r.Validations.FirstOrDefault(v => string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase))
        ?? r.Validations.FirstOrDefault(v => v.PartyRole is null);

    /// <summary>
    /// Selección del PASO DE IDENTIDAD / expediente / panel consolidado: la ÚLTIMA coincidencia del rol.
    /// </summary>
    private static BiometricValidationDto? UltimaDelRol(BiometricValidationsResponse r, string parte, bool esTraspaso)
    {
        var matches = r.Validations.Where(v => esTraspaso
            ? string.Equals(v.PartyRole, parte, StringComparison.OrdinalIgnoreCase)
            : v.PartyRole is null || string.Equals(v.PartyRole, "comprador", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count > 0 ? matches[^1] : null;
    }

    // ── AC1 — VID referenciada sin intentos locales ─────────────────────────────

    [Fact]
    public async Task CompradorConVidReferenciadaYSinIntentosLocales_SeMuestraAprobada()
    {
        var ct = TestContext.Current.CancellationToken;
        var instance = Instancia(Guid.NewGuid(), TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        var (handler, id) = Handler(instance, ("1020304050", VidVigenteDeOtroTramite("1020304050")));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle();
        PrimeraDelRol(result, "comprador")!.Status.Should().Be(BiometricEstados.Aprobado);
        PrimeraDelRol(result, "comprador")!.PartyRole.Should().Be("comprador");
        result.SupersededValidations.Should().BeNull();
    }

    // ── AC2 / AC7 — la VID vigente le gana a los intentos locales ───────────────

    [Theory]
    [InlineData(BiometricEstados.Rechazado)]
    [InlineData(BiometricEstados.Expirado)]
    [InlineData(BiometricEstados.EnProceso)]
    public async Task ConIntentoLocalNoVigente_PrevaleceLaVidReferenciada(string estadoLocal)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        var intentoLocal = Validacion("comprador", "1020304050", estadoLocal, Ayer, procedureInstanceId: id);
        instance.BiometricValidations.Add(intentoLocal);
        var (handler, _) = Handler(instance, ("1020304050", VidVigenteDeOtroTramite("1020304050")));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        // El resumen (primera coincidencia) y el paso de Identidad (última) deben coincidir: aprobada.
        PrimeraDelRol(result!, "comprador")!.Status.Should().Be(BiometricEstados.Aprobado);
        UltimaDelRol(result!, "comprador", esTraspaso: false)!.Status.Should().Be(BiometricEstados.Aprobado);
        PrimeraDelRol(result!, "comprador")!.Id.Should()
            .Be(UltimaDelRol(result!, "comprador", esTraspaso: false)!.Id, "el estado de la parte es UNO solo");
        // El intento no desaparece de la respuesta: viaja como histórico relegado.
        result!.SupersededValidations.Should().ContainSingle().Which.Id.Should().Be(intentoLocal.Id);
    }

    [Fact]
    public async Task ConAprobadaLocalYaVencida_PrevaleceLaVidReferenciada()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        // Aprobada pero con la vigencia YA vencida (valid_until en el pasado): no puede representar a la parte.
        var vencida = Validacion("comprador", "1020304050", BiometricEstados.Aprobado, Ayer.AddDays(-60),
            validatedAt: Ayer.AddDays(-60), validUntil: DateTimeOffset.UtcNow.AddDays(-30),
            procedureInstanceId: id);
        instance.BiometricValidations.Add(vencida);
        var vid = VidVigenteDeOtroTramite("1020304050");
        var (handler, _) = Handler(instance, ("1020304050", vid));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        PrimeraDelRol(result!, "comprador")!.Id.Should().Be(vid.Id);
        UltimaDelRol(result!, "comprador", esTraspaso: false)!.Id.Should().Be(vid.Id);
        result!.SupersededValidations.Should().ContainSingle().Which.Id.Should().Be(vencida.Id);
    }

    // ── AC4 — sin identidad vigente NO se inventa nada ──────────────────────────

    [Fact]
    public async Task SinVidVigente_ElIntentoRechazadoSigueVisibleYNoHayAprobacion()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        var rechazado = Validacion("comprador", "1020304050", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id);
        instance.BiometricValidations.Add(rechazado);
        var (handler, _) = Handler(instance, ("1020304050", null));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle().Which.Status.Should().Be(BiometricEstados.Rechazado);
        result.SupersededValidations.Should().BeNull("sin identidad vigente no se relega nada");
    }

    [Fact]
    public async Task ConVidVigenteDeOtraPersona_NoSeApalanca()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        instance.BiometricValidations.Add(
            Validacion("comprador", "1020304050", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id));
        // La identidad vigente existe, pero para OTRO documento: la consulta por el documento del actor
        // no devuelve nada y la parte sigue sin validar.
        var (handler, _) = Handler(instance, ("1020304050", null), ("9999999999", VidVigenteDeOtroTramite("9999999999")));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Should().OnlyContain(v => v.Status == BiometricEstados.Rechazado);
    }

    // ── AC5 — traspaso: comprador y vendedor apalancados ────────────────────────

    [Fact]
    public async Task TraspasoConAmbasPartesReferenciadas_LasDosQuedanAprobadas()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.Traspaso);
        instance.Actors.Add(ActorNatural("comprador", "1111111111"));
        instance.Actors.Add(ActorNatural("vendedor", "2222222222"));
        instance.BiometricValidations.Add(
            Validacion("comprador", "1111111111", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id));
        instance.BiometricValidations.Add(
            Validacion("vendedor", "2222222222", BiometricEstados.Expirado, Ayer, procedureInstanceId: id));
        var (handler, _) = Handler(instance,
            ("1111111111", VidVigenteDeOtroTramite("1111111111", rolOrigen: "vendedor")),
            ("2222222222", VidVigenteDeOtroTramite("2222222222", rolOrigen: "comprador")));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        foreach (var parte in new[] { "comprador", "vendedor" })
        {
            PrimeraDelRol(result!, parte)!.Status.Should().Be(BiometricEstados.Aprobado, "parte {0}", parte);
            UltimaDelRol(result!, parte, esTraspaso: true)!.Status.Should()
                .Be(BiometricEstados.Aprobado, "parte {0}", parte);
        }

        result!.SupersededValidations.Should().HaveCount(2);
    }

    // ── Validación PROPIA vigente: sigue mandando (no se consulta otro trámite) ──

    [Fact]
    public async Task ConAprobadaPropiaVigenteYReintentoPosteriorRechazado_PrevaleceLaAprobadaPropia()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        var aprobada = Validacion("comprador", "1020304050", BiometricEstados.Aprobado, Ayer.AddDays(-2),
            validatedAt: Ayer.AddDays(-2), validUntil: DateTimeOffset.UtcNow.AddDays(28),
            procedureInstanceId: id);
        instance.BiometricValidations.Add(aprobada);
        instance.BiometricValidations.Add(
            Validacion("comprador", "1020304050", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id));
        var (handler, _) = Handler(instance, ("1020304050", null));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        PrimeraDelRol(result!, "comprador")!.Id.Should().Be(aprobada.Id);
        UltimaDelRol(result!, "comprador", esTraspaso: false)!.Id.Should().Be(aprobada.Id);
        result!.SupersededValidations.Should().ContainSingle();
    }

    // ── Correcciones de code review ────────────────────────────────────

    [Fact]
    public async Task O1_SinNadaQueRelegar_ElListadoConservaSuOrden()
    {
        // El corte temprano llevaba `&& prevalecientes.Count == 0`, condición INALCANZABLE, así que la
        // lista se reordenaba aunque no hubiera un solo intento que relegar. Con dos aprobadas vigentes
        // de la misma parte (ninguna se relega) el orden por fecha debe quedar intacto.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        var primera = Validacion("comprador", "1020304050", BiometricEstados.Aprobado, Ayer.AddDays(-5),
            validatedAt: Ayer.AddDays(-5), validUntil: DateTimeOffset.UtcNow.AddDays(25), procedureInstanceId: id);
        var segunda = Validacion("comprador", "1020304050", BiometricEstados.Aprobado, Ayer.AddDays(-1),
            validatedAt: Ayer.AddDays(-1), validUntil: DateTimeOffset.UtcNow.AddDays(29), procedureInstanceId: id);
        instance.BiometricValidations.Add(primera);
        instance.BiometricValidations.Add(segunda);
        var (handler, _) = Handler(instance, ("1020304050", null));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Select(v => v.Id).Should().Equal(primera.Id, segunda.Id);
        result.SupersededValidations.Should().BeNull();
    }

    [Fact]
    public async Task O2_IntentoRechazadoSinRolDeOtraPersona_SigueVisibleParaElGestor()
    {
        // En matrícula la parte "comprador" recoge también las filas sin rol, pero SIN comparar el
        // documento se relegaba el rechazo de OTRA persona: desaparecía de la vista principal y el
        // gestor dejaba de verlo. Ahora solo se relegan las filas sin rol del MISMO documento.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        var ajeno = Validacion(null, "9999999999", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id);
        var propioRechazado = Validacion(null, "1020304050", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id);
        instance.BiometricValidations.Add(ajeno);
        instance.BiometricValidations.Add(propioRechazado);
        var (handler, _) = Handler(instance, ("1020304050", VidVigenteDeOtroTramite("1020304050")));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Select(v => v.Id).Should().Contain(ajeno.Id, "el rechazo de otra persona no se oculta");
        result.SupersededValidations!.Select(v => v.Id).Should().Equal(propioRechazado.Id);
    }

    [Fact]
    public async Task O3_TraspasoConFilaDelMismoTramiteRotuladaConElOtroRol_QuedaAsociadaALaParte()
    {
        // FindVigenteApprovedByDocumentAsync busca por documento en TODO el tenant y puede devolver una
        // fila de ESTE mismo trámite rotulada con el otro rol (p. ej. el vendedor se capturó primero
        // como comprador). Antes se marcaba como prevaleciente y subía a la posición 0, pero los
        // consumidores emparejan POR ROL y seguían sin ver identidad vigente para esa parte.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.Traspaso);
        instance.Actors.Add(ActorNatural("comprador", "1111111111"));
        instance.Actors.Add(ActorNatural("vendedor", "2222222222"));
        // Fila de este trámite con el documento del VENDEDOR pero rotulada como comprador.
        var malRotulada = Validacion("comprador", "2222222222", BiometricEstados.Aprobado, Ayer.AddDays(-2),
            validatedAt: Ayer.AddDays(-2), validUntil: DateTimeOffset.UtcNow.AddDays(28), procedureInstanceId: id);
        instance.BiometricValidations.Add(malRotulada);
        var intentoVendedor = Validacion("vendedor", "2222222222", BiometricEstados.Rechazado, Ayer, procedureInstanceId: id);
        instance.BiometricValidations.Add(intentoVendedor);
        var (handler, _) = Handler(instance, ("1111111111", null), ("2222222222", malRotulada));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        PrimeraDelRol(result!, "vendedor")!.Id.Should().Be(malRotulada.Id);
        UltimaDelRol(result!, "vendedor", esTraspaso: true)!.Id.Should().Be(malRotulada.Id);
        result!.SupersededValidations!.Select(v => v.Id).Should().Equal(intentoVendedor.Id);
    }

    [Fact]
    public async Task O3_LaEtiquetaLegitimaDeLaOtraParte_NoSeRoba()
    {
        // Borde del arreglo anterior: si las DOS partes tienen el mismo documento, la fila rotulada con
        // el otro rol es legítimamente suya y re-rotularla dejaría a esa parte sin identidad vigente.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.Traspaso);
        instance.Actors.Add(ActorNatural("comprador", "3333333333"));
        instance.Actors.Add(ActorNatural("vendedor", "3333333333"));
        var delVendedor = Validacion("vendedor", "3333333333", BiometricEstados.Aprobado, Ayer.AddDays(-2),
            validatedAt: Ayer.AddDays(-2), validUntil: DateTimeOffset.UtcNow.AddDays(28), procedureInstanceId: id);
        instance.BiometricValidations.Add(delVendedor);
        var (handler, _) = Handler(instance, ("3333333333", delVendedor));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle().Which.PartyRole.Should().Be("vendedor");
    }

    [Fact]
    public async Task ConFilaPropiaSinRolQueEsLaVigente_NoSeDuplicaEnElListado()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var instance = Instancia(id, TramiteModalidadEntradaCodes.MatriculaInicial);
        instance.Actors.Add(ActorNatural("comprador", "1020304050"));
        // Fila histórica SIN rol (matrícula antigua): la consulta por documento la devuelve tal cual.
        var sinRol = Validacion(null, "1020304050", BiometricEstados.Aprobado, Ayer.AddDays(-3),
            validatedAt: Ayer.AddDays(-3), validUntil: DateTimeOffset.UtcNow.AddDays(27),
            procedureInstanceId: id);
        instance.BiometricValidations.Add(sinRol);
        var (handler, _) = Handler(instance, ("1020304050", sinRol));

        var (result, error) = await handler.HandleAsync(id, TenantId, ct);

        error.Should().BeNull();
        result!.Validations.Should().ContainSingle().Which.Id.Should().Be(sinRol.Id);
    }
}
