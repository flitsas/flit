import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { AuditoriaDictamen } from "@/components/backoffice/AuditoriaDictamen";
import type { VersionDictamen } from "@/lib/backoffice/api";
import { EMPRESA_A, LOTE, llamadas, simularApi } from "@/lib/backoffice/__tests__/datos";

// Uso de ejemplo: <AuditoriaDictamen diagnosticoId={id} vehiculoId={vid} empresa={empresa} /> (solo backoffice, D6) pide
// GET /backoffice/diagnosticos/{id}/vehiculos/{vid}/dictamen/versiones?empresa= y lista las versiones de la más nueva a
// la más vieja con origen, usuario, motivo, fecha, alertamientos agregados/editados y eliminados, y la marca «Superado por
// recálculo» (D7). Cuatro estados: cargando, error con Reintentar, vacío y lleno.

const VEHICULO = "vehiculo-1";
const PATH = `/backoffice/diagnosticos/${LOTE}/vehiculos/${VEHICULO}/dictamen/versiones`;
const RUTA = `GET ${PATH}`;

const automatica: VersionDictamen = {
  version: 3,
  veredicto: "BLOQUEADO",
  origen: "AUTOMATICO",
  usuario: null,
  motivo: null,
  en: "2026-11-10T14:00:00+00:00",
  superado_por_recalculo: false,
  alertamientos: [{ codigo: "SOAT_NO_VIGENTE", texto: "El SOAT no está vigente.", efecto: "BLOQUEA", fuente: "RUNT", ajuste: null }],
  eliminados: [],
};
const ajuste: VersionDictamen = {
  version: 4,
  veredicto: "VIABLE",
  origen: "AJUSTE_FLIT",
  usuario: { id: "00000000-0000-7000-8000-0000000000f5", nombre: "Soporte de prueba" },
  motivo: "Soporte verificó el SOAT con la aseguradora",
  en: "2026-11-11T09:30:00+00:00",
  superado_por_recalculo: true,
  alertamientos: [{ codigo: "AJUSTE_FLIT", texto: "SOAT renovado, pendiente de reflejarse en RUNT.", efecto: "ADVIERTE", fuente: "RUNT", ajuste: "AGREGADO" }],
  eliminados: [{ codigo: "SOAT_NO_VIGENTE", texto: "El SOAT no está vigente.", efecto: "BLOQUEA", fuente: "RUNT" }],
};

const montar = () => render(<AuditoriaDictamen diagnosticoId={LOTE} vehiculoId={VEHICULO} empresa={EMPRESA_A} />);

describe("HU14562 AC47 auditoría de versiones del dictamen (solo backoffice)", () => {
  it("pide las versiones con ?empresa= y las muestra en orden descendente con origen, usuario, motivo y fecha", async () => {
    const api = simularApi({ [RUTA]: () => Response.json({ versiones: [automatica, ajuste] }) });
    montar();
    const lista = await screen.findByRole("list", { name: "Versiones del dictamen" });
    const items = within(lista).getAllByRole("listitem", { name: /^Versión/ });
    expect(items.map((i) => i.getAttribute("aria-label"))).toEqual(["Versión 4", "Versión 3"]);
    expect(items[0]).toHaveTextContent("Ajuste FLIT");
    expect(items[0]).toHaveTextContent("Soporte de prueba");
    expect(items[0]).toHaveTextContent("Soporte verificó el SOAT con la aseguradora");
    expect(items[0]).toHaveTextContent("11/11/2026");
    expect(items[1]).toHaveTextContent("Automático");
    expect(new URL(String(llamadas(api, "GET", PATH)[0][0]), "http://localhost").searchParams.get("empresa")).toBe(EMPRESA_A);
  });

  it("muestra los alertamientos agregados o editados y los eliminados, y «Superado por recálculo»", async () => {
    simularApi({ [RUTA]: () => Response.json({ versiones: [ajuste, automatica] }) });
    montar();
    const v4 = await screen.findByRole("listitem", { name: "Versión 4" });
    expect(within(v4).getByText("Superado por recálculo")).toBeInTheDocument();
    expect(within(v4).getByRole("list", { name: "Alertamientos de la versión 4" })).toHaveTextContent("Agregado");
    expect(within(v4).getByRole("list", { name: "Eliminados en la versión 4" })).toHaveTextContent("El SOAT no está vigente.");
  });

  it("vacío: sin versiones", async () => {
    simularApi({ [RUTA]: () => Response.json({ versiones: [] }) });
    montar();
    expect(await screen.findByText("Este vehículo aún no tiene dictamen.")).toBeInTheDocument();
  });

  it("error con Reintentar que vuelve a pedir", async () => {
    let n = 0;
    const api = simularApi({ [RUTA]: () => (n++ === 0 ? new Response(null, { status: 503 }) : Response.json({ versiones: [automatica] })) });
    montar();
    fireEvent.click(await screen.findByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("listitem", { name: "Versión 3" })).toBeInTheDocument();
    expect(llamadas(api, "GET", PATH)).toHaveLength(2);
  });

  it("cargando: anuncia la carga", () => {
    simularApi({ [RUTA]: () => new Promise<Response>(() => {}) });
    montar();
    expect(screen.getByRole("status", { name: /cargando/i })).toBeInTheDocument();
  });
});
