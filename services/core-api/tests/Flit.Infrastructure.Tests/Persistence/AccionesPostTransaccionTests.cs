using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Infrastructure.Tests.Persistence;

/// <summary>
/// HU #12797 (Épica #12760, F2) — <see cref="AccionesPostTransaccion"/>: el doble del scope de la
/// transacción ambiente de <c>OtClientProcedureRepository.ExecuteIn*TenantScopeAsync</c>. El SQL de ese
/// método es <c>set_config</c> de PostgreSQL y este proyecto no tiene base relacional; aquí se ejercita
/// la mecánica que ese método invoca: <c>Abrir</c> → acciones diferidas → <c>CerrarAsync</c> tras el
/// commit o el rollback.
/// <para>Uso de ejemplo: <c>acciones.Abrir(txId); acciones.TryDiferir(txId, borrar, compensar);
/// await acciones.CerrarAsync(txId, confirmada: true);</c></para>
/// </summary>
public sealed class AccionesPostTransaccionTests
{
    [Fact]
    public async Task Confirmada_EjecutaSoloLasDeConfirmacion_DespuesDelCierre()
    {
        var acciones = new AccionesPostTransaccion();
        var tx = Guid.NewGuid();
        var log = new List<string>();
        acciones.Abrir(tx);

        acciones.TryDiferir(tx, Registrar(log, "borrar_anterior"), Registrar(log, "borrar_nuevo")).Should().BeTrue();
        log.Should().BeEmpty("nada corre antes del commit real");

        await acciones.CerrarAsync(tx, confirmada: true);

        log.Should().Equal("borrar_anterior");
        acciones.Pendientes.Should().Be(0);
    }

    [Fact]
    public async Task Revertida_EjecutaSoloLasDeReversion()
    {
        var acciones = new AccionesPostTransaccion();
        var tx = Guid.NewGuid();
        var log = new List<string>();
        acciones.Abrir(tx);
        acciones.TryDiferir(tx, Registrar(log, "borrar_anterior"), Registrar(log, "borrar_nuevo"));
        acciones.TryDiferir(tx, Registrar(log, "bitacora"), Registrar(log, "bitacora"));

        await acciones.CerrarAsync(tx, confirmada: false);

        log.Should().Equal("borrar_nuevo", "bitacora");
    }

    [Fact]
    public void SinTransaccionOTransaccionAjena_NoDifiere()
    {
        var acciones = new AccionesPostTransaccion();
        acciones.TryDiferir(null, () => Task.CompletedTask, null).Should().BeFalse("sin transacción gestionada");

        acciones.Abrir(Guid.NewGuid());
        acciones.TryDiferir(null, () => Task.CompletedTask, null).Should().BeFalse("el contexto no tiene transacción");
        acciones.TryDiferir(Guid.NewGuid(), () => Task.CompletedTask, null).Should().BeFalse(
            "otra transacción no llamaría a CerrarAsync: la acción quedaría colgada");
        acciones.Pendientes.Should().Be(0);
    }

    [Fact]
    public async Task UnaAccionQueLanza_NoImpideLasDemas_NiTumbaElCierre()
    {
        var acciones = new AccionesPostTransaccion();
        var tx = Guid.NewGuid();
        var log = new List<string>();
        acciones.Abrir(tx);
        acciones.TryDiferir(tx, () => throw new InvalidOperationException("storage caído"), null);
        acciones.TryDiferir(tx, Registrar(log, "segunda"), null);

        var cerrar = () => acciones.CerrarAsync(tx, confirmada: true);

        await cerrar.Should().NotThrowAsync();
        log.Should().Equal("segunda");
    }

    [Fact]
    public async Task CierreDeOtraTransaccion_NoEjecutaNiDescarta()
    {
        var acciones = new AccionesPostTransaccion();
        var tx = Guid.NewGuid();
        var log = new List<string>();
        acciones.Abrir(tx);
        acciones.TryDiferir(tx, Registrar(log, "x"), null);

        await acciones.CerrarAsync(Guid.NewGuid(), confirmada: true);

        log.Should().BeEmpty();
        acciones.Pendientes.Should().Be(1);
    }

    [Fact]
    public async Task Reintento_AbrirDescartaLoDelIntentoAnterior()
    {
        // La estrategia de reintentos re-ejecuta el bloque: un intento abortado no puede arrastrar
        // borrados a la transacción del siguiente.
        var acciones = new AccionesPostTransaccion();
        var log = new List<string>();
        var intento1 = Guid.NewGuid();
        acciones.Abrir(intento1);
        acciones.TryDiferir(intento1, Registrar(log, "intento1"), null);

        var intento2 = Guid.NewGuid();
        acciones.Abrir(intento2);
        await acciones.CerrarAsync(intento2, confirmada: true);

        log.Should().BeEmpty();
    }

    // ── Re-review #12760 (N2 / L-N2) — commit de resultado desconocido ─────────────────────────────

    [Fact]
    public async Task CommitDesconocido_NoBorraNada_NiElAnteriorNiElNuevo_YLaBitacoraSeEscribeIgual()
    {
        var acciones = new AccionesPostTransaccion();
        var tx = Guid.NewGuid();
        var log = new List<string>();
        acciones.Abrir(tx);
        acciones.TryDiferir(tx, Registrar(log, "borrar_anterior"), Registrar(log, "borrar_nuevo")).Should().BeTrue();
        acciones.TryDiferir(tx, Registrar(log, "borrar_anterior_2"), null).Should().BeTrue();
        acciones.TryDiferirSiempre(tx, Registrar(log, "bitacora")).Should().BeTrue();

        var omitidas = await acciones.CerrarAsync(tx, FinTransaccion.Desconocida);

        log.Should().Equal(["bitacora"], "sin saber qué referencia la BD, cualquier borrado podría ser el del binario vivo");
        omitidas.Should().Be(2, "el repositorio lo registra (solo ids) para recuperar el huérfano");
        acciones.Pendientes.Should().Be(0);
    }

    [Theory]
    [InlineData(true, "borrar_anterior")]
    [InlineData(false, "borrar_nuevo")]
    public async Task BitacoraSiempre_SeEscribeUnaSolaVez_EnCommitYEnRollback(bool confirmada, string borrado)
    {
        var acciones = new AccionesPostTransaccion();
        var tx = Guid.NewGuid();
        var log = new List<string>();
        acciones.Abrir(tx);
        acciones.TryDiferirSiempre(tx, Registrar(log, "bitacora"));
        acciones.TryDiferir(tx, Registrar(log, "borrar_anterior"), Registrar(log, "borrar_nuevo"));

        var omitidas = await acciones.CerrarAsync(tx, confirmada ? FinTransaccion.Confirmada : FinTransaccion.Revertida);

        omitidas.Should().Be(0);
        log.Should().Equal(["bitacora", borrado], "el rollback explícito conserva la compensación actual");
    }

    [Fact]
    public void TryDiferirSiempre_SinTransaccionGestionada_NoDifiere()
    {
        var acciones = new AccionesPostTransaccion();
        acciones.TryDiferirSiempre(Guid.NewGuid(), () => Task.CompletedTask).Should().BeFalse();
        acciones.Abrir(Guid.NewGuid());
        acciones.TryDiferirSiempre(null, () => Task.CompletedTask).Should().BeFalse();
        acciones.Pendientes.Should().Be(0);
    }

    [Fact]
    public void Repositorio_SinTransaccionAmbiente_NoDifiere_YMapeaElConflictoDeConcurrencia()
    {
        using var db = new FlitDbContext(
            new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(nameof(AccionesPostTransaccionTests)).Options);
        var repo = new ProcedureInstanceRepository(db);

        repo.TryDeferUntilTransactionEnds(() => { }).Should().BeFalse(
            "sin transacción gestionada el llamador borra de inmediato, como antes");
        repo.IsConcurrencyConflict(new DbUpdateConcurrencyException("0 filas")).Should().BeTrue();
        repo.IsConcurrencyConflict(new InvalidOperationException()).Should().BeFalse();
    }

    private static Func<Task> Registrar(List<string> log, string nombre) => () =>
    {
        log.Add(nombre);
        return Task.CompletedTask;
    };
}
