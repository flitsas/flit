import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Sesión local que el hub ya no respalda (se cerró desde otro producto, o entró otro usuario): la app pide una nueva
// al hub en vez de mostrar «Tu sesión expiró» o «sin productos» con el usuario anterior.

function fakeWindow(path = "/tramites?estado=abierto") {
  const store = new Map<string, string>();
  const assign = vi.fn();
  vi.stubGlobal("window", {
    location: { pathname: path.split("?")[0], search: path.includes("?") ? `?${path.split("?")[1]}` : "", assign },
    sessionStorage: {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => void store.set(k, v),
    },
  });
  return { assign, store };
}

async function load() {
  vi.resetModules();
  return (await import("../client")).reauthenticate;
}

describe("reauthenticate", () => {
  beforeEach(() => vi.useFakeTimers({ now: new Date("2026-10-02T10:00:00Z") }));
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("pide una sesión nueva al hub y vuelve a la ruta actual", async () => {
    const { assign } = fakeWindow();
    const reauthenticate = await load();

    expect(reauthenticate()).toBe(true);
    expect(assign).toHaveBeenCalledWith("/auth/login?returnTo=%2Ftramites%3Festado%3Dabierto");
  });

  it("en silencio (prompt=none) cuando la página sabe qué mostrar sin sesión", async () => {
    const { assign } = fakeWindow("/");
    const reauthenticate = await load();

    reauthenticate({ returnTo: "/?inicio=1", silent: true });
    expect(assign).toHaveBeenCalledWith("/auth/login?prompt=none&returnTo=%2F%3Finicio%3D1");
  });

  it("varias llamadas que fallan a la vez redirigen una sola vez", async () => {
    const { assign } = fakeWindow();
    const reauthenticate = await load();

    expect(reauthenticate()).toBe(true);
    expect(reauthenticate()).toBe(true);
    expect(assign).toHaveBeenCalledTimes(1);
  });

  it("no insiste si ya se intentó hace unos segundos (bucle) pero sí en el siguiente cambio de usuario", async () => {
    const { assign, store } = fakeWindow();
    store.set("flit:reauth-at", String(Date.now() - 3_000));
    let reauthenticate = await load();

    expect(reauthenticate()).toBe(false);
    expect(assign).not.toHaveBeenCalled();

    // Dos cambios de usuario seguidos, como los de la prueba manual (27 s entre uno y otro).
    store.set("flit:reauth-at", String(Date.now() - 27_000));
    reauthenticate = await load();
    expect(reauthenticate()).toBe(true);
  });
});
