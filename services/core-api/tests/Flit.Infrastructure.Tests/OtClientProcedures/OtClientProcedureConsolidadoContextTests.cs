using Flit.Admin.Domain.DocumentOrderOverrides;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.OtClientProcedures;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Flit.Infrastructure.Tests.OtClientProcedures;

/// <summary>
/// HU #13389 (Épica #13216) — servicio compartido del acceso del OT, el scope de la compañía cliente y la
/// precedencia de la matriz del consolidado maestro (<see cref="OtClientProcedureConsolidadoContext"/>).
/// Sin HTTP ni PostgreSQL: el repositorio OT y el resolver de la matriz son sustitutos. Lo que filtra el
/// acceso (organismo, borrador, borrado lógico, grant) es la consulta del repositorio; aquí se fija que el
/// servicio la usa tal cual y que «no accesible» es <c>null</c>, nunca una excepción ni un resultado HTTP.
/// <para>Uso de ejemplo:
/// <code>
/// var access = await ctx.ResolverAccesoAsync(tenantOt, id, transitOfficeIdOverride: null, ct);
/// if (access is null) return acceso_revocado;
/// var r = await ctx.EjecutarEnContextoClienteAsync(access, precedencia => generar(precedencia), ct);
/// </code></para>
/// </summary>
public sealed class OtClientProcedureConsolidadoContextTests
{
    private static readonly Guid TenantOt = Guid.Parse("e0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid ClientTenant = Guid.Parse("e0000000-0000-4000-8000-0000000000b2");
    private static readonly Guid Organismo = Guid.Parse("e0000000-0000-4000-8000-0000000000e5");

    private readonly IOtClientProcedureRepository _repo = Substitute.For<IOtClientProcedureRepository>();
    private readonly IResolvedDocumentMatrixResolver _matrix = Substitute.For<IResolvedDocumentMatrixResolver>();
    private readonly List<string> _log = [];
    private bool _enScopeCliente;

    public OtClientProcedureConsolidadoContextTests()
    {
        _repo.ExecuteInClientTenantScopeAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                _log.Add($"scope:{ci.ArgAt<Guid>(0)}");
                _enScopeCliente = true;
                try
                {
                    return await ci.ArgAt<Func<Task<string>>>(1)();
                }
                finally
                {
                    _enScopeCliente = false;
                }
            });
    }

    private OtClientProcedureConsolidadoContext Sut() => new(_repo, _matrix);

    private static OtClientProcedure Access(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        ClientTenantId = ClientTenant,
        ProcedureTypeId = Guid.Parse("e0000000-0000-4000-8000-0000000000f6"),
        TransitOfficeId = Organismo,
    };

    // ── AC3 — acceso de dominio sin HTTP ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AC3_Accesible_DevuelveElTramite_ConElTenantOtYSinOverride()
    {
        var ct = TestContext.Current.CancellationToken;
        var access = Access();
        _repo.GetByIdAsync(TenantOt, access.Id, null, Arg.Any<CancellationToken>()).Returns(access);

        var resultado = await Sut().ResolverAccesoAsync(TenantOt, access.Id, null, ct);

        resultado.Should().BeSameAs(access);
        await _repo.Received(1).GetByIdAsync(TenantOt, access.Id, null, ct);
    }

    [Fact]
    public async Task AC3_SuperAdmin_PasaElOrganismoIndicadoComoOverride()
    {
        var ct = TestContext.Current.CancellationToken;
        var access = Access();
        _repo.GetByIdAsync(TenantOt, access.Id, Organismo, Arg.Any<CancellationToken>()).Returns(access);

        var resultado = await Sut().ResolverAccesoAsync(TenantOt, access.Id, Organismo, ct);

        resultado.Should().BeSameAs(access);
        await _repo.DidNotReceive().GetByIdAsync(TenantOt, access.Id, null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("otro_organismo")]
    [InlineData("borrador")]
    [InlineData("borrado_logico")]
    public async Task AC3_NoAccesible_DevuelveNull_SinLanzar(string motivo)
    {
        // La consulta del repositorio (misma regla que la bandeja) es la que excluye cada caso: el
        // servicio solo no la debe traducir a nada distinto de null.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(TenantOt, id, Organismo, Arg.Any<CancellationToken>()).Returns((OtClientProcedure?)null);

        var act = async () => await Sut().ResolverAccesoAsync(TenantOt, id, Organismo, ct);

        (await act.Should().NotThrowAsync()).Subject.Should().BeNull(motivo);
        await _repo.DidNotReceiveWithAnyArgs().ExecuteInClientTenantScopeAsync<string>(default, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AC3_Contrato_NingunMetodoExponeTiposHttp()
    {
        var tipos = typeof(IOtClientProcedureConsolidadoContext).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .SelectMany(Desplegar)
            .ToList();

        tipos.Should().NotContain(t => (t.Namespace ?? string.Empty).StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        typeof(IOtClientProcedureConsolidadoContext).GetMethod(nameof(IOtClientProcedureConsolidadoContext.ResolverAccesoAsync))!
            .ReturnType.Should().Be<Task<OtClientProcedure?>>();

        static IEnumerable<Type> Desplegar(Type t) =>
            t.IsGenericType ? t.GetGenericArguments().SelectMany(Desplegar).Append(t) : [t];
    }

    // ── AC4 — precedencia resuelta dentro del scope del cliente ───────────────────────────────────

    [Fact]
    public async Task AC4_LaAccionRecibeLaPrecedencia_ResueltaDentroDelScopeDelTenantCliente()
    {
        var ct = TestContext.Current.CancellationToken;
        var access = Access();
        _matrix.ResolveAsync(access.ProcedureTypeId, Organismo, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _log.Add($"matriz:{(_enScopeCliente ? "en-scope" : "fuera")}");
                return Matriz("soat", "factura");
            });
        IReadOnlyList<string>? recibida = null;

        var resultado = await Sut().EjecutarEnContextoClienteAsync(
            access,
            precedencia =>
            {
                _log.Add($"accion:{(_enScopeCliente ? "en-scope" : "fuera")}");
                recibida = precedencia;
                return Task.FromResult("ok");
            },
            ct);

        resultado.Should().Be("ok");
        recibida.Should().Equal("soat", "factura");
        _log.Should().Equal($"scope:{ClientTenant}", "matriz:en-scope", "accion:en-scope");
        await _repo.Received(1).ExecuteInClientTenantScopeAsync(ClientTenant, Arg.Any<Func<Task<string>>>(), ct);
    }

    [Fact]
    public async Task AC4_SiElResolverDeLaMatrizFalla_LaAccionRecibeListaVacia()
    {
        var ct = TestContext.Current.CancellationToken;
        _matrix.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("matriz caída"));
        IReadOnlyList<string>? recibida = null;

        var resultado = await Sut().EjecutarEnContextoClienteAsync(
            Access(),
            precedencia => { recibida = precedencia; return Task.FromResult("ok"); },
            ct);

        resultado.Should().Be("ok");
        recibida.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task AC4_SinMatrizConfigurada_LaAccionRecibeListaVacia()
    {
        var ct = TestContext.Current.CancellationToken;
        _matrix.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Matriz());
        IReadOnlyList<string>? recibida = null;

        await Sut().EjecutarEnContextoClienteAsync(
            Access(), precedencia => { recibida = precedencia; return Task.FromResult("ok"); }, ct);

        recibida.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task AC4_LaExcepcionDeLaAccionSePropagaDesdeElScope()
    {
        var ct = TestContext.Current.CancellationToken;
        _matrix.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(Matriz());

        var act = () => Sut().EjecutarEnContextoClienteAsync<string>(
            Access(), _ => throw new InvalidOperationException("storage_unavailable"), ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("storage_unavailable");
    }

    [Fact]
    public async Task AC4_Variante_SinResolverPrecedencia_PasaNull_YNoLlamaAlResolver()
    {
        // La entrega que no va a generar (sinGenerar o tipo consolidado) no resuelve la matriz.
        var ct = TestContext.Current.CancellationToken;
        IReadOnlyList<string>? recibida = ["centinela"];

        var resultado = await Sut().EjecutarEnContextoClienteAsync(
            Access(),
            resolverPrecedencia: false,
            precedencia =>
            {
                _log.Add($"accion:{(_enScopeCliente ? "en-scope" : "fuera")}");
                recibida = precedencia;
                return Task.FromResult("ok");
            },
            ct);

        resultado.Should().Be("ok");
        recibida.Should().BeNull();
        _log.Should().Equal($"scope:{ClientTenant}", "accion:en-scope");
        await _matrix.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC4_Variante_ConResolverPrecedencia_IgualQueLaPrincipal()
    {
        var ct = TestContext.Current.CancellationToken;
        var access = Access();
        _matrix.ResolveAsync(access.ProcedureTypeId, Organismo, Arg.Any<CancellationToken>()).Returns(Matriz("soat"));
        IReadOnlyList<string>? recibida = null;

        await Sut().EjecutarEnContextoClienteAsync(
            access, resolverPrecedencia: true, precedencia => { recibida = precedencia; return Task.FromResult("ok"); }, ct);

        recibida.Should().Equal("soat");
    }

    [Fact]
    public async Task Contrato_ArgumentosNulos_Lanzan()
    {
        var sut = Sut();

        await FluentActions.Awaiting(() => sut.EjecutarEnContextoClienteAsync<string>(null!, _ => Task.FromResult("x")))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => sut.EjecutarEnContextoClienteAsync<string>(Access(), (Func<IReadOnlyList<string>, Task<string>>)null!))
            .Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => new OtClientProcedureConsolidadoContext(null!, _matrix))
            .Should().Throw<ArgumentNullException>();
    }

    private static Task<IReadOnlyList<ResolvedDocumentMatrixItem>> Matriz(params string[] codigos) =>
        Task.FromResult<IReadOnlyList<ResolvedDocumentMatrixItem>>(
            codigos.Select(c => new ResolvedDocumentMatrixItem { Codigo = c }).ToList());
}
