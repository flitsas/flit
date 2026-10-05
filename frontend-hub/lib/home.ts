// Inicio del hub, opción 4 «Hub con entrada directa» (B-11, HU #12987; plan maestro §4.6, provisional). Función pura.
import type { SuiteApp } from "@flit/shell/apps";

export type HomeDecision = { kind: "direct"; url: string } | { kind: "home"; products: SuiteApp[] };

/**
 * Quien puede abrir UN solo producto entra directo a él; con dos o más (o ninguno), el inicio del hub. `forceHome`
 * (`?inicio=1`) es la vuelta explícita al hub desde el menú de productos: nunca redirige.
 */
export function decideHome(apps: SuiteApp[], forceHome: boolean): HomeDecision {
  const products = apps.filter((app) => app.code !== "plataforma");
  if (!forceHome && products.length === 1) return { kind: "direct", url: products[0].url };
  return { kind: "home", products };
}
