using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PersonaNatural;

/// <summary>HU #14216 — el caso de uso del canal no pasa por el grant del portal.</summary>
public sealed class CanalPuntoHandlerTests
{
    private readonly Repo _repo = new();
    private readonly Audit _audit = new();

    [Fact]
    public async Task EncenderUnOrganismoOperable_EscribeElCanal_YNoElGrant()
    {
        var error = await Handler().EstablecerAsync(Repo.Id, true, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        _repo.LlamadasCanal.Should().Be(1);
        _repo.UltimoEncendido.Should().BeTrue();
        _repo.LlamadasGrant.Should().Be(0);
        _audit.Escrituras.Should().Be(1);
    }

    [Fact]
    public async Task ApagarUnCanalApagado_RespondeAplicado_SinVolverAAuditar()
    {
        _repo.Canal = new CambioHabilitacionPn(true, false);

        var error = await Handler().EstablecerAsync(Repo.Id, false, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        _repo.LlamadasCanal.Should().Be(1);
        _repo.UltimoEncendido.Should().BeFalse();
        _repo.LlamadasGrant.Should().Be(0);
        _audit.Escrituras.Should().Be(0);
    }

    [Fact]
    public async Task EncenderUnoQueNoEsOperable_Rechaza_SinEscribir()
    {
        _repo.Operable = false;

        var error = await Handler().EstablecerAsync(Repo.Id, true, Guid.NewGuid(), TestContext.Current.CancellationToken);

        error.Should().Be(ConfiguracionPnErrores.OrganismoNoOperable);
        _repo.LlamadasCanal.Should().Be(0);
        _repo.LlamadasGrant.Should().Be(0);
    }

    [Fact]
    public async Task UnOrganismoQueNoExiste_RespondeNoExiste_AlEncenderYAlApagar()
    {
        _repo.Existe = false;
        _repo.Operable = false;
        _repo.Canal = new CambioHabilitacionPn(false, false);

        (await Handler().EstablecerAsync(Repo.Id, true, Guid.NewGuid(), TestContext.Current.CancellationToken))
            .Should().Be(ConfiguracionPnErrores.OrganismoNoExiste);
        (await Handler().EstablecerAsync(Repo.Id, false, Guid.NewGuid(), TestContext.Current.CancellationToken))
            .Should().Be(ConfiguracionPnErrores.OrganismoNoExiste);
        _repo.LlamadasGrant.Should().Be(0);
    }

    private CanalPuntoPnHandler Handler() => new(_repo, _audit);

    private sealed class Audit : IAdminAuditWriter
    {
        public int Escrituras;

        public Task WriteAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Escrituras++;
            return Task.CompletedTask;
        }
    }

    private sealed class Repo : IPersonaNaturalConfigRepository
    {
        public static readonly Guid Id = Guid.Parse("0199b7e0-0000-7000-8000-0000000000aa");
        public bool Operable = true;
        public bool Existe = true;
        public CambioHabilitacionPn Canal = new(true, false);
        public int LlamadasCanal;
        public int LlamadasGrant;
        public bool? UltimoEncendido;

        public Task<bool> OrganismoEsOperableAsync(Guid organismoId, CancellationToken ct = default) => Task.FromResult(Operable);

        public Task<bool> ExisteOrganismoAsync(Guid organismoId, CancellationToken ct = default) => Task.FromResult(Existe);

        public Task<CambioHabilitacionPn> EstablecerCanalPuntoAsync(Guid organismoId, bool encendido, Guid actor, CancellationToken ct = default)
        {
            LlamadasCanal++;
            UltimoEncendido = encendido;
            return Task.FromResult(Canal);
        }

        public Task<CambioHabilitacionPn> EstablecerOrganismoAsync(Guid organismoId, bool habilitado, Guid actor, CancellationToken ct = default)
        {
            LlamadasGrant++;
            return Task.FromResult(new CambioHabilitacionPn(true, false));
        }

        public Task<CanalPuntoResolucion> ResolverCanalPuntoAsync(string codigo, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ParametrosPn> ObtenerParametrosAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GuardarParametrosPnResultado> GuardarParametrosAsync(ParametrosPn nuevos, long rowVersionEsperada, Guid actor, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OrganismoPnView>> ListarOrganismosAsync(bool soloHabilitados, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TipoPnView>> ListarTiposAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CambioHabilitacionPn> EstablecerTipoAsync(Guid tipoId, bool habilitado, Guid actor, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TipoEsCandidatoAsync(Guid tipoId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
