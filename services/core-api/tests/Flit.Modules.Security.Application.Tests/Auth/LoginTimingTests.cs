using System.Diagnostics;
using Flit.Admin.Application.Auditing;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #12422 AC2/AC8 (ADR-0060 D3) — el tiempo de respuesta de un login RECHAZADO no debe delatar
/// si el email existe. El escenario realista de ataque es una credencial INCORRECTA (el atacante no
/// conoce la contraseña real): en ese camino el handler SIEMPRE llama a
/// <see cref="IPasswordHasher.Verify"/> (contra el hash real o el señuelo,
/// <see cref="IPasswordHasher.DummyHash"/>) y devuelve el MISMO <see cref="InvalidCredentialsException"/>
/// ANTES de tocar <see cref="ITenantNetworkMembership"/> — el chequeo de red solo se alcanza con
/// contraseña correcta (documentado abajo, <see cref="Login_ContrasenaCorrectaFueraDeRed_HaceUnaLlamadaAdicional"/>:
/// no es un vector de enumeración porque exige conocer YA la contraseña).
/// <c>Uso de ejemplo</c>: <see cref="MeasureMedianMs"/> hashea <paramref name="samples"/> veces con
/// un <see cref="IPasswordHasher"/> de coste FIJO (Argon2 real de bajo coste, no simulado) y devuelve
/// la mediana en milisegundos.
/// <para>
/// <see cref="TraitAttribute"/> "Timing": sensible al entorno de CI (carga de la máquina). Tolerancia
/// amplia (ratio ≤ 2.0) a propósito: el objetivo es detectar una regresión estructural (un camino que
/// se salta el hash), no medir microsegundos. 1.6 fallaba en runners cargados (~1.84 observado).
/// </para>
/// </summary>
[Trait("Category", "Timing")]
public sealed class LoginTimingTests
{
    private const int Samples = 20;
    private const double ToleranceRatio = 2.0;

    private static readonly Guid HeadA = Guid.NewGuid();
    private static readonly Guid OffNetworkTenant = Guid.NewGuid();

    // Argon2 REAL de bajo coste — mismo algoritmo que producción, parámetros reducidos para que la
    // suite no tarde minutos, pero SIN inventar un simulador (el propio Verify es el que se mide).
    private readonly LowCostArgon2PasswordHasher _hasher = new();

    [Fact]
    public async Task Login_ConCredencialIncorrecta_TiempoEquivalenteEntreInexistenteYFueraDeRed()
    {
        var repository = Substitute.For<IAuthUserRepository>();
        var membership = Substitute.For<ITenantNetworkMembership>();
        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        var auditWriter = Substitute.For<IAdminAuditWriter>();
        var domainContext = Substitute.For<IDomainContextAccessor>();
        domainContext.Kind.Returns(DomainKind.Network);
        domainContext.Host.Returns("app.red-a.com");
        domainContext.HeadTenantId.Returns(HeadA);

        var realHash = _hasher.Hash("TheRealPassword1!");
        repository.FindByEmailAsync("ghost@flit.local", Arg.Any<CancellationToken>())
            .Returns((UserAuthSnapshot?)null);
        repository.FindByEmailAsync("offnetwork@empresa.com", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "offnetwork@empresa.com",
                Status = "active",
                PasswordHash = realHash,
                TenantId = OffNetworkTenant,
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "demo_admin")],
            });
        membership.ResolveAsync(OffNetworkTenant, Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None);

        var handlerFor = new LoginHandler(
            repository, _hasher, jwtIssuer, auditWriter, NullAuditContextAccessor.Instance,
            membership, domainContext);

        var inexistente = await MeasureMedianMsAsync(
            () => handlerFor.HandleAsync(new LoginCommand("ghost@flit.local", "WrongPass1!"), CancellationToken.None));
        var fueraDeRed = await MeasureMedianMsAsync(
            () => handlerFor.HandleAsync(new LoginCommand("offnetwork@empresa.com", "WrongPass1!"), CancellationToken.None));

        var ratio = Math.Max(inexistente, fueraDeRed) / Math.Min(inexistente, fueraDeRed);
        ratio.Should().BeLessOrEqualTo(
            ToleranceRatio,
            "usuario inexistente ({0} ms) y usuario existente fuera de la red ({1} ms) deben tomar tiempo equivalente " +
            "(AC2/AC8): ambos siempre verifican el hash (real o señuelo) y ninguno llega a consultar la red " +
            "porque la contraseña es incorrecta.",
            inexistente, fueraDeRed);
    }

    /// <summary>
    /// Documenta el ÚNICO camino asimétrico a propósito: con la contraseña CORRECTA de un usuario
    /// fuera de la red, el handler SÍ hace una llamada adicional a <see cref="ITenantNetworkMembership"/>
    /// antes de rechazar. No es un vector de enumeración: exige conocer la contraseña real, que ya
    /// identifica al usuario sin necesidad de medir tiempos.
    /// </summary>
    [Fact]
    public async Task Login_ContrasenaCorrectaFueraDeRed_HaceUnaLlamadaAdicional()
    {
        var repository = Substitute.For<IAuthUserRepository>();
        var membership = Substitute.For<ITenantNetworkMembership>();
        var jwtIssuer = Substitute.For<IJwtTokenIssuer>();
        var auditWriter = Substitute.For<IAdminAuditWriter>();
        var domainContext = Substitute.For<IDomainContextAccessor>();
        domainContext.Kind.Returns(DomainKind.Network);
        domainContext.Host.Returns("app.red-a.com");
        domainContext.HeadTenantId.Returns(HeadA);

        var realHash = _hasher.Hash("TheRealPassword1!");
        repository.FindByEmailAsync("offnetwork@empresa.com", Arg.Any<CancellationToken>())
            .Returns(new UserAuthSnapshot
            {
                UserId = Guid.NewGuid(),
                Email = "offnetwork@empresa.com",
                Status = "active",
                PasswordHash = realHash,
                TenantId = OffNetworkTenant,
                ActiveRoles = [new UserRoleSnapshot(Guid.NewGuid(), "demo_admin")],
            });
        membership.ResolveAsync(OffNetworkTenant, Arg.Any<CancellationToken>())
            .Returns(NetworkMembership.None);

        var handler = new LoginHandler(
            repository, _hasher, jwtIssuer, auditWriter, NullAuditContextAccessor.Instance,
            membership, domainContext);

        var act = () => handler.HandleAsync(
            new LoginCommand("offnetwork@empresa.com", "TheRealPassword1!"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        await membership.Received(1).ResolveAsync(OffNetworkTenant, Arg.Any<CancellationToken>());
    }

    private static async Task<double> MeasureMedianMsAsync(Func<Task> attempt)
    {
        var samples = new List<double>(Samples);
        for (var i = 0; i < Samples; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await attempt();
            }
            catch (InvalidCredentialsException)
            {
                // Resultado esperado — lo que se mide es cuánto tarda en llegar aquí.
            }
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return samples[samples.Count / 2];
    }

    /// <summary>
    /// Argon2 REAL (mismo formato que <c>Flit.Infrastructure.Security.Argon2PasswordHasher</c>, que
    /// esta capa no puede referenciar — Application no depende de Infrastructure) con parámetros de
    /// coste reducidos para que la suite corra en milisegundos y no en segundos, preservando la
    /// propiedad que se pone a prueba: el señuelo hashea con el MISMO costo que un hash real.
    /// </summary>
    private sealed class LowCostArgon2PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;

        private static readonly Lazy<string> DummyHashValue = new(() =>
        {
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SaltSize);
            var hash = HashPassword("timing-equalizer", salt);
            return $"argon2id|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
        });

        public string DummyHash => DummyHashValue.Value;

        public string Hash(string password)
        {
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SaltSize);
            var hash = HashPassword(password, salt);
            return $"argon2id|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
        }

        public bool Verify(string password, string storedHash)
        {
            var parts = storedHash.Split('|');
            if (parts.Length != 3)
                return false;

            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = HashPassword(password, salt);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        private static byte[] HashPassword(string password, byte[] salt)
        {
            var argon2 = new Konscious.Security.Cryptography.Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = 2,
                MemorySize = 8192,
                Iterations = 1,
            };
            return argon2.GetBytes(HashSize);
        }
    }
}
