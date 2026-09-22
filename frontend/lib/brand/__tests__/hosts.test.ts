import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * Uso de ejemplo: `isFlitHost("app.movilidadandina.com")` → `false` con la lista por defecto;
 * `isFlitHost("dev.flitsas.online")` → `true` (matchea `*.flitsas.online`).
 */
async function loadIsFlitHost(envValue?: string) {
  vi.resetModules();
  if (envValue === undefined) {
    vi.unstubAllEnvs();
  } else {
    vi.stubEnv("NEXT_PUBLIC_FLIT_HOSTS", envValue);
  }
  const mod = await import("../hosts");
  return mod.isFlitHost;
}

describe("isFlitHost — AC1/AC7 #12419", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it("sin NEXT_PUBLIC_FLIT_HOSTS, usa el respaldo por defecto (localhost)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("localhost:3000")).toBe(true);
    expect(isFlitHost("127.0.0.1:3000")).toBe(true);
  });

  it("respaldo por defecto matchea *.flitsas.online y *.flitsas.com", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("dev.flitsas.online")).toBe(true);
    expect(isFlitHost("api.pdn.flitsas.com")).toBe(true);
  });

  it("un dominio de red (fuera de la lista) NO es FLIT (edge case)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("app.movilidadandina.com")).toBe(false);
  });

  it("respeta NEXT_PUBLIC_FLIT_HOSTS explícita (contrato)", async () => {
    const isFlitHost = await loadIsFlitHost("miplataforma.com,*.miplataforma.com");
    expect(isFlitHost("miplataforma.com")).toBe(true);
    expect(isFlitHost("app.miplataforma.com")).toBe(true);
    expect(isFlitHost("flitsas.online")).toBe(false);
  });

  it("ignora mayúsculas y el puerto del host header", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost("LOCALHOST:4001")).toBe(true);
  });

  it("host null/vacío se trata como FLIT (respaldo seguro, AC5)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    expect(isFlitHost(null)).toBe(true);
    expect(isFlitHost("")).toBe(true);
  });

  it("*.dominio NO matchea el dominio raíz sin subdominio (contrato)", async () => {
    const isFlitHost = await loadIsFlitHost("*.flitsas.online");
    expect(isFlitHost("flitsas.online")).toBe(false);
    expect(isFlitHost("dev.flitsas.online")).toBe(true);
  });
});

/**
 * Uso de ejemplo: con `NEXT_PUBLIC_FLIT_HOSTS="*.flitsas.online,!marcablancadev.flitsas.online"`,
 * `isFlitHost("marcablancadev.flitsas.online")` → `false` (host de prueba de marca blanca).
 */
describe("isFlitHost — excepciones con '!' (AC6/AC8 #12761)", () => {
  // Lista tal como queda horneada en el build del frontend (cd.yml + .env.example).
  const HOSTS_CON_NEGACIONES =
    "*.flitsas.online,*.flitsas.com,!marcablancadev.flitsas.online,!marcablancaqa.flitsas.online,!marcablancapdn.flitsas.online";

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it.each([
    "marcablancadev.flitsas.online",
    "marcablancaqa.flitsas.online",
    "marcablancapdn.flitsas.online",
  ])("el host de prueba %s NO es FLIT (happy path)", async (host) => {
    const isFlitHost = await loadIsFlitHost(HOSTS_CON_NEGACIONES);
    expect(isFlitHost(host)).toBe(false);
  });

  it("los demás dominios propios siguen siendo FLIT (no regresión)", async () => {
    const isFlitHost = await loadIsFlitHost(HOSTS_CON_NEGACIONES);
    expect(isFlitHost("app.flitsas.online")).toBe(true);
    expect(isFlitHost("cualquiera.flitsas.com")).toBe(true);
  });

  it("el dominio raíz flitsas.online conserva el comportamiento del comodín", async () => {
    const isFlitHost = await loadIsFlitHost(HOSTS_CON_NEGACIONES);
    // `*.flitsas.online` nunca cubrió el dominio raíz: sigue sin serlo, la negación no lo altera.
    expect(isFlitHost("flitsas.online")).toBe(false);
  });

  it("la negación es exacta: no cubre subdominios del host negado (edge case)", async () => {
    const isFlitHost = await loadIsFlitHost(HOSTS_CON_NEGACIONES);
    expect(isFlitHost("sub.marcablancadev.flitsas.online")).toBe(true);
  });

  it("la negación gana aunque el patrón positivo aparezca antes en la lista (precedencia)", async () => {
    const isFlitHost = await loadIsFlitHost(
      "marcablancadev.flitsas.online,*.flitsas.online,!marcablancadev.flitsas.online",
    );
    expect(isFlitHost("marcablancadev.flitsas.online")).toBe(false);
  });

  it("aplica la negación tras quitar el puerto y normalizar mayúsculas (edge case)", async () => {
    const isFlitHost = await loadIsFlitHost(HOSTS_CON_NEGACIONES);
    expect(isFlitHost("marcablancadev.flitsas.online:3000")).toBe(false);
    expect(isFlitHost("MarcaBlancaDev.Flitsas.Online")).toBe(false);
  });

  it("sin NEXT_PUBLIC_FLIT_HOSTS el respaldo por defecto TAMBIÉN excluye los hosts de prueba (contrato)", async () => {
    const isFlitHost = await loadIsFlitHost(undefined);
    // Las negaciones viven también en DEFAULT_FLIT_HOSTS: si el build-arg falta o llega vacío,
    // los hosts de prueba siguen siendo dominio de red (no pueden depender de la variable).
    expect(isFlitHost("marcablancadev.flitsas.online")).toBe(false);
    expect(isFlitHost("marcablancaqa.flitsas.online")).toBe(false);
    expect(isFlitHost("marcablancapdn.flitsas.online")).toBe(false);
    // El resto del respaldo no cambia respecto de #12419 (no regresión).
    expect(isFlitHost("localhost:3000")).toBe(true);
    expect(isFlitHost("127.0.0.1:3000")).toBe(true);
    expect(isFlitHost("dev.flitsas.online")).toBe(true);
    expect(isFlitHost("app.movilidadandina.com")).toBe(false);
  });

  it("con lista vacía o en blanco cae al respaldo, que conserva las negaciones (edge case)", async () => {
    const isFlitHost = await loadIsFlitHost("   ");
    expect(isFlitHost("marcablancadev.flitsas.online")).toBe(false);
    expect(isFlitHost("dev.flitsas.online")).toBe(true);
  });

  it("host vacío/null sigue asumiéndose FLIT con negaciones presentes (AC5 #12419)", async () => {
    const isFlitHost = await loadIsFlitHost(HOSTS_CON_NEGACIONES);
    expect(isFlitHost(null)).toBe(true);
    expect(isFlitHost("")).toBe(true);
  });

  it("una lista de solo negaciones no convierte a nadie en FLIT (contrato)", async () => {
    const isFlitHost = await loadIsFlitHost("!marcablancadev.flitsas.online");
    expect(isFlitHost("marcablancadev.flitsas.online")).toBe(false);
    expect(isFlitHost("app.flitsas.online")).toBe(false);
  });
});
