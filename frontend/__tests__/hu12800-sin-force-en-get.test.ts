/**
 * Épica #12760 (code-review G2) — ningún componente pasa `force` a las rutas GET de entrega del
 * consolidado (`entregarConsolidado` / `entregarOtConsolidado`). El backend rechaza `force` en los
 * GET (400 `force_no_permitido_en_get`): forzar es exclusivo de los POST de generación
 * (`generarConsolidado(…, true)` del gestor, `generarOtConsolidadoMaestro(…, true)` del OT).
 *
 * Guardia estática: recorre el código de UI (`app/`, `components/`, `lib/` salvo los clientes HTTP
 * que declaran la opción) y busca llamadas a la entrega cuyos argumentos mencionen `force`.
 *
 * Uso de ejemplo:
 *   llamadasEntregaConForce("await entregarOtConsolidado(id, scope, { force: true });") // → 1
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

const RAIZ = join(__dirname, '..');
const CARPETAS = ['app', 'components', 'lib'];
/** Clientes HTTP: declaran la opción `force` (queda disponible, pero ningún componente la usa). */
const EXCLUIDOS = new Set(['lib/api/tramites-client.ts', 'lib/api/admin-ot.ts']);

function archivos(dir: string): string[] {
  const out: string[] = [];
  for (const nombre of readdirSync(dir)) {
    const ruta = join(dir, nombre);
    if (statSync(ruta).isDirectory()) {
      if (nombre === '__tests__' || nombre === 'node_modules') continue;
      out.push(...archivos(ruta));
    } else if (/\.(ts|tsx)$/.test(nombre) && !/\.(test|spec)\.tsx?$/.test(nombre)) {
      out.push(ruta);
    }
  }
  return out;
}

/** Número de llamadas a la entrega (gestor u OT) cuyos argumentos contienen `force`. */
function llamadasEntregaConForce(codigo: string): number {
  const patron = /entregar(?:Ot)?Consolidado\s*\(([^;]*?)\)\s*[;,)]/g;
  let n = 0;
  for (const m of codigo.matchAll(patron)) {
    if (/\bforce\b/.test(m[1] ?? '')) n += 1;
  }
  return n;
}

describe('G2 — sin force en las rutas GET de entrega', () => {
  it('el detector encuentra una llamada con force y no marca una sin force', () => {
    expect(llamadasEntregaConForce('await entregarOtConsolidado(id, scope, { force: true });')).toBe(1);
    expect(
      llamadasEntregaConForce("await tramitesClient.entregarConsolidado(id, { tipo: 'consolidado' });"),
    ).toBe(0);
    expect(llamadasEntregaConForce('')).toBe(0);
  });

  it('ningún archivo de UI pasa force a entregarConsolidado / entregarOtConsolidado', () => {
    const infractores = CARPETAS.flatMap((c) => archivos(join(RAIZ, c)))
      .map((ruta) => relative(RAIZ, ruta).replace(/\\/g, '/'))
      .filter((rel) => !EXCLUIDOS.has(rel))
      .filter((rel) => llamadasEntregaConForce(readFileSync(join(RAIZ, rel), 'utf-8')) > 0);
    expect(infractores).toEqual([]);
  });
});
