import { existsSync, readdirSync, statSync } from "node:fs";
import { join, relative, sep } from "node:path";
import { describe, expect, it } from "vitest";

import { getArticleBySlug, getNavTree, MANUAL_ARTICLES, searchManualArticles, visibleAudiences } from "@/lib/manual/catalog";
import { MANUAL_VERSION, MANUAL_VERSION_DATE } from "@/lib/manual/articles/meta";
import type { ManualAudience } from "@/lib/manual/types";

/**
 * HU #14580 (FE-7) — DR. FLIT documenta el Dashboard, los Reportes, la navegación nueva del dock y los cambios de la
 * ola (modal de 640 px, menú «Acciones», cabecera RUNT/SIMIT y carga en una columna), y cubre las pantallas de
 * Diagnóstico que aún no tenían texto.
 *
 * Uso de ejemplo:
 *   getArticleBySlug("6-diagnostico/13-dashboard")?.audience   // "Gestor"
 *   searchManualArticles("exportar reporte a excel", 3)         // incluye "6-diagnostico/14-reportes"
 */

const NUEVOS: { slug: string; audience: ManualAudience }[] = [
  { slug: "6-diagnostico/13-dashboard", audience: "Gestor" },
  { slug: "6-diagnostico/14-reportes", audience: "Gestor" },
  { slug: "6-diagnostico/15-vista-global-flit", audience: "Super Admin" },
];

const textoDe = (slug: string) => {
  const a = getArticleBySlug(slug);
  if (!a) return "";
  return [
    a.title,
    a.summary,
    ...a.blocks.flatMap((b) => [
      b.title,
      ...b.paragraphs,
      ...(b.bullets ?? []),
      ...(b.callouts ?? []).flatMap((c) => [c.title ?? "", c.text]),
      ...(b.media ?? []).map((m) => m.alt),
    ]),
  ].join(" ");
};

const SECCION = () => MANUAL_ARTICLES.filter((a) => a.sectionId === "diagnostico");

describe("manual/diagnostico (HU #14580) — artículos nuevos y actualizados", () => {
  it("los artículos nuevos existen en la sección, con su audiencia, después de los 12 de la HU #14577", () => {
    for (const e of NUEVOS) {
      const a = getArticleBySlug(e.slug);
      expect(a, e.slug).toBeDefined();
      expect(a!.sectionId, e.slug).toBe("diagnostico");
      expect(a!.audience, e.slug).toBe(e.audience);
    }
    const slugs = getNavTree().find((n) => n.section.id === "diagnostico")!.articles.map((a) => a.slug);
    expect(slugs.slice(12)).toEqual(NUEVOS.map((e) => e.slug));
  });

  it("Dashboard: los 6 KPIs, el criterio de fecha, el periodo por defecto y el tope de un año", () => {
    const t = textoDe("6-diagnostico/13-dashboard");
    for (const kpi of ["Diagnósticos", "Vehículos", "Consultas a fuentes", "Tiempo promedio de consulta", "Saneamientos solicitados", "Trámites creados"])
      expect(t, kpi).toContain(`«${kpi}»`);
    expect(t).toMatch(/fecha de creación/i);
    expect(t).toMatch(/fecha de (la )?solicitud/i);
    expect(t).toMatch(/últimos 30 días/);
    expect(t).toContain("El rango no puede superar un año");
    for (const bloque of ["Uso en el tiempo", "Veredictos", "Tipo de análisis", "Salud de las fuentes"]) expect(t, bloque).toContain(bloque);
  });

  it("Dashboard: vista de la compañía frente a vista global, con el selector «Compañía» solo para FLIT", () => {
    const t = textoDe("6-diagnostico/13-dashboard");
    expect(t).toContain("Resumen de diagnósticos de");
    expect(t).toMatch(/Todas las compañías/);
    const flit = textoDe("6-diagnostico/15-vista-global-flit");
    for (const frase of ["«Compañía»", "«Todas las compañías»", "Vista global · todas las compañías", "Compañías con más uso"]) expect(flit, frase).toContain(frase);
  });

  it("Reportes: el catálogo de 5 reportes, sus columnas, el aviso de la Ley 1581 y el registro de accesos", () => {
    const t = textoDe("6-diagnostico/14-reportes");
    for (const r of ["Vehículos diagnosticados", "Consultas a fuentes", "Saneamientos", "Derivaciones a trámite", "Consumo por compañía"])
      expect(t, r).toContain(r);
    for (const col of ["Propietarios (documento)", "Locatario (documento)", "Veredicto", "Fuente", "Días hasta el cierre"]) expect(t, col).toContain(col);
    expect(t).toContain("Ley 1581 de 2012");
    expect(t).toMatch(/queda registrada con tu usuario/);
    expect(t).toMatch(/no incluye(n)? (el )?nombre/i);
  });

  it("Reportes: exportar Excel y PDF, límites 5.000 / 1.000 (como los pinta la UI, CR-FE-9) y los mensajes de error en lenguaje de usuario", () => {
    const t = textoDe("6-diagnostico/14-reportes");
    for (const frase of ["«Exportar Excel»", "«Exportar PDF»", "5.000", "1.000", "«Acota el filtro: máximo 5.000 registros en Excel»", "Ya hay una exportación en curso", "25, 50 o 100"])
      expect(t, frase).toContain(frase);
    expect(t).not.toMatch(/\b\d{1,3} 000\b/);
    expect(t).toMatch(/todo el filtro|todos los registros del filtro/i);
    // Nada de jerga interna de seguridad ni códigos HTTP en el texto del usuario.
    expect(t).not.toMatch(/\b(422|429|accesos_reporte|SP-\d+|tenant)\b/);
  });

  it("navegación: el Dashboard al entrar, «Diagnósticos» y «Reportes» en el dock y los enlaces viejos redirigidos", () => {
    const t = textoDe("6-diagnostico/1-que-es");
    expect(t).toMatch(/Dashboard/);
    expect(t).toContain("«Diagnósticos»");
    expect(t).toContain("«Reportes»");
    expect(t).toMatch(/enlaces? (guardados|antiguos|viejos)/i);
    expect(textoDe("6-diagnostico/2-inicio-y-vehiculos")).toContain("«Diagnósticos» del dock");
  });

  it("los artículos existentes reflejan el modal, el menú «Acciones», la cabecera RUNT/SIMIT y la carga en una columna", () => {
    const tablero = textoDe("6-diagnostico/4-tablero-y-dictamen");
    expect(tablero).toContain("«Acciones»");
    expect(tablero).toMatch(/RUNT[\s\S]*SIMIT[\s\S]*«vehículo», «propietario» y «locatario»/);
    expect(tablero).toMatch(/ventana/);
    const carga = textoDe("6-diagnostico/3-nuevo-diagnostico");
    expect(carga).toMatch(/una sola columna/);
    expect(carga).toMatch(/«Información general», «Modo» y «Alcance»/);
    const san = textoDe("6-diagnostico/10-saneamiento");
    expect(san).toContain("«Solicitar saneamiento»");
    expect(san).toMatch(/ventana «Deuda y saneamiento»/);
    expect(san).toMatch(/clic fuera/);
    expect(textoDe("6-diagnostico/6-evidencias-y-avisos")).toContain("«Descargar PDF»");
    expect(textoDe("6-diagnostico/7-crear-tramite")).toMatch(/«Ver trámite #N»/);
    // Lo que ya no existe en pantalla no se sigue describiendo.
    for (const a of SECCION()) {
      const x = textoDe(a.slug);
      expect(x, a.slug).not.toMatch(/panel lateral|columna Reporte|«Reintentar placa»|Al entrar ves el título «Diagnóstico vehicular»/);
    }
  });
});

/**
 * Inventario de pantallas de frontend-diagnostico/app: cada `page.tsx` tiene un artículo que la nombra. Si aparece una
 * pantalla nueva sin fila aquí, el test falla y obliga a documentarla.
 */
const APP_DIR = join(__dirname, "..", "..", "..", "..", "frontend-diagnostico", "app");

const PANTALLAS: Record<string, { slug: string; marca: string | RegExp }> = {
  "(app)": { slug: "6-diagnostico/13-dashboard", marca: "«Dashboard»" },
  "(app)/diagnosticos": { slug: "6-diagnostico/2-inicio-y-vehiculos", marca: "«Diagnósticos» del dock" },
  "(app)/diagnosticos/nuevo": { slug: "6-diagnostico/3-nuevo-diagnostico", marca: "«Nuevo diagnóstico»" },
  "(app)/diagnosticos/[id]": { slug: "6-diagnostico/4-tablero-y-dictamen", marca: "Semáforo de consultas del lote" },
  "(app)/diagnosticos/[id]/carga": { slug: "6-diagnostico/3-nuevo-diagnostico", marca: "«Cargar vehículos»" },
  "(app)/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]": { slug: "6-diagnostico/4-tablero-y-dictamen", marca: "Respuesta de la fuente" },
  "(app)/reportes": { slug: "6-diagnostico/14-reportes", marca: "«Reportes»" },
  "(app)/configuracion": { slug: "6-diagnostico/8-configuracion", marca: "«Ajustes → Configuración»" },
  "(app)/backoffice/diagnosticos": { slug: "6-diagnostico/9-backoffice-flit", marca: "Lotes por compañía" },
  "(app)/backoffice/diagnosticos/[id]": { slug: "6-diagnostico/9-backoffice-flit", marca: "Al abrir un lote" },
  "(app)/backoffice/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]": { slug: "6-diagnostico/9-backoffice-flit", marca: /detalle de la consulta/i },
  "(app)/backoffice/fuentes-obligatorias": { slug: "6-diagnostico/9-backoffice-flit", marca: "«Fuentes obligatorias»" },
  "(app)/backoffice/saneamientos": { slug: "6-diagnostico/11-saneamiento-gestion-flit", marca: "bandeja «Saneamientos»" },
  "(app)/backoffice/saneamientos/[sid]": { slug: "6-diagnostico/11-saneamiento-gestion-flit", marca: "Al abrir una fila" },
  "403": { slug: "6-diagnostico/1-que-es", marca: "No tienes permisos en Diagnóstico" },
};

function paginas(dir: string): string[] {
  const out: string[] = [];
  for (const nombre of readdirSync(dir)) {
    const ruta = join(dir, nombre);
    if (statSync(ruta).isDirectory()) out.push(...paginas(ruta));
    else if (nombre === "page.tsx") out.push(relative(APP_DIR, dir).split(sep).join("/"));
  }
  return out;
}

describe("manual/diagnostico (HU #14580) — lo que faltaba: cada pantalla tiene su texto", () => {
  it("el inventario de páginas coincide con la tabla de pantallas documentadas", () => {
    expect(existsSync(APP_DIR), APP_DIR).toBe(true);
    expect(paginas(APP_DIR).sort()).toEqual(Object.keys(PANTALLAS).sort());
  });

  for (const [ruta, { slug, marca }] of Object.entries(PANTALLAS)) {
    it(`/${ruta} → ${slug}`, () => {
      const t = textoDe(slug);
      if (typeof marca === "string") expect(t).toContain(marca);
      else expect(t).toMatch(marca);
    });
  }
});

describe("manual/diagnostico (HU #14580) — búsqueda, diagrama, versión y privacidad", () => {
  const CASOS: { pregunta: string; perfil: "gestor" | "admin_company" | "superadmin"; slug: string }[] = [
    { pregunta: "dashboard de diagnóstico", perfil: "gestor", slug: "6-diagnostico/13-dashboard" },
    { pregunta: "reportes de diagnóstico", perfil: "gestor", slug: "6-diagnostico/14-reportes" },
    { pregunta: "exportar reporte de diagnóstico", perfil: "gestor", slug: "6-diagnostico/14-reportes" },
    { pregunta: "datos personales en los reportes", perfil: "gestor", slug: "6-diagnostico/14-reportes" },
  ];
  for (const c of CASOS) {
    it(`«${c.pregunta}» (${c.perfil}) → ${c.slug}`, () => {
      const hits = searchManualArticles(c.pregunta, 3, { audiences: visibleAudiences(c.perfil) });
      expect(hits.map((h) => h.slug)).toContain(c.slug);
    });
  }

  it("hay un diagrama «dashboard y reportes por rol» con su .mmd y su .svg, usado en un artículo de la sección", () => {
    const id = "diagnostico-dashboard-reportes";
    expect(existsSync(join(__dirname, "..", "diagrams", `${id}.mmd`))).toBe(true);
    expect(existsSync(join(__dirname, "..", "..", "..", "public", "manual", "diagrams", `${id}.svg`))).toBe(true);
    const usado = SECCION().some((a) => a.blocks.some((b) => (b.media ?? []).some((m) => m.kind === "diagram" && m.id === id)));
    expect(usado).toBe(true);
  });

  it("versión del manual 2.3.0 de noviembre de 2026", () => {
    expect(MANUAL_VERSION).toBe("2.3.0");
    expect(MANUAL_VERSION_DATE).toBe("Noviembre 2026");
  });

  it("sin marcadores pendientes ni placas que no sean de ejemplo en los artículos nuevos", () => {
    for (const e of NUEVOS) {
      const t = textoDe(e.slug);
      expect(t, e.slug).not.toMatch(/\b(TODO|TBD|XXX)\b/);
      expect((t.match(/\b[A-Z]{3}\d{3}\b/g) ?? []).filter((p) => p !== "ABC123"), e.slug).toEqual([]);
      expect(t, e.slug).not.toMatch(/\b(CC|NIT) \d{4,}/);
    }
  });
});
