import { describe, expect, it } from "vitest";
import nextConfig from "@/next.config";

// Uso de ejemplo: next.config.ts `redirects()` lleva los enlaces viejos del listado (/?vista=vehiculos, /?estado=…) a
// /diagnosticos con la misma query (Next.js la conserva al redirigir) y deja /?desde=&hasta= en el Dashboard (diseño §c).

const CLAVES_DEL_LISTADO = ["vista", "modo", "estado", "nombre", "pagina", "tamano", "placa", "veredicto", "saneamiento", "saneamiento_estado", "con_tramite"];

type Regla = { source: string; destination: string; permanent: boolean; has?: Array<{ type: string; key: string }> };

async function reglas(): Promise<Regla[]> {
  expect(nextConfig.redirects).toBeTypeOf("function");
  return (await nextConfig.redirects!()) as Regla[];
}

/** Simula el emparejamiento de Next.js para la raíz: la primera regla cuyo `has` de query casa con la URL. */
function destino(rs: Regla[], url: string): string | null {
  const u = new URL(url, "http://localhost");
  const regla = rs.find((r) => r.source === u.pathname && (r.has ?? []).every((h) => h.type === "query" && u.searchParams.has(h.key)));
  return regla ? `${regla.destination}${u.search}` : null;
}

describe("redirecciones de compatibilidad (FE-4, escenario «enlaces viejos»)", () => {
  it("una regla temporal por cada clave exclusiva del listado, de / a /diagnosticos", async () => {
    const rs = await reglas();
    for (const clave of CLAVES_DEL_LISTADO) {
      expect(rs).toContainEqual({ source: "/", has: [{ type: "query", key: clave }], destination: "/diagnosticos", permanent: false });
    }
  });

  it("los enlaces viejos llegan a /diagnosticos con la misma query", async () => {
    const rs = await reglas();
    expect(destino(rs, "/?vista=vehiculos&placa=ABC")).toBe("/diagnosticos?vista=vehiculos&placa=ABC");
    expect(destino(rs, "/?estado=COMPLETADO&pagina=2")).toBe("/diagnosticos?estado=COMPLETADO&pagina=2");
  });

  it("la raíz sin query y con solo desde/hasta/empresa se queda en el Dashboard", async () => {
    const rs = await reglas();
    expect(destino(rs, "/")).toBeNull();
    expect(destino(rs, "/?desde=2026-10-01&hasta=2026-10-31&empresa=x")).toBeNull();
  });
});
