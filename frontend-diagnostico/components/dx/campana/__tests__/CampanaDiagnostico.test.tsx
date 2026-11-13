import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { CampanaDiagnostico } from "@/components/dx/campana/CampanaDiagnostico";
import { pedirSondeoNotificaciones } from "@/lib/notificaciones/useNotificaciones";
import { EVIDENCIA, LOTE, aviso, bandeja, bandejas, pedidos, simularApi } from "@/lib/notificaciones/__tests__/datos";

// Uso de ejemplo: <CampanaDiagnostico /> en headerActions de DiagnosticoShell: botón «Avisos» con el contador de no
// leídas (oculto en 0), panel con la lista (título, mensaje, fecha y acción), vacío, error con Reintentar y «Marcar
// todas como leídas»; cada ZIP_EVIDENCIAS_LISTO nuevo que llega en un sondeo abre una NotificacionPersistente con
// «Descargar» (endpoint de evidencias existente) que la marca leída.

const h = vi.hoisted(() => ({ descargar: vi.fn() }));
vi.mock("@/lib/reportes/api", async (original) => ({ ...(await original<typeof import("@/lib/reportes/api")>()), descargarArchivo: h.descargar }));

const RUTA = "/notificaciones";
beforeEach(() => h.descargar.mockReset().mockResolvedValue("evidencias.zip"));
afterEach(() => vi.unstubAllGlobals());

const abrir = async () => {
  fireEvent.click(await screen.findByRole("button", { name: /^Avisos/ }));
  return screen.findByRole("region", { name: "Avisos" });
};

describe("HU14562 AC50 campana en la barra", () => {
  it("contador oculto en 0", async () => {
    simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([])) });
    render(<CampanaDiagnostico />);
    const boton = await screen.findByRole("button", { name: "Avisos" });
    await waitFor(() => expect(boton).toHaveAttribute("data-cargado", "true"));
    expect(screen.queryByText(/sin leer/)).toBeNull();
  });

  it("muestra «N avisos sin leer» y el panel con título, mensaje, fecha y acción", async () => {
    simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([aviso(1), aviso(2, { leida_en: "2026-11-12T16:00:00+00:00" })])) });
    render(<CampanaDiagnostico />);
    expect(await screen.findByRole("button", { name: "Avisos, 1 aviso sin leer" })).toHaveAttribute("aria-expanded", "false");
    const panel = await abrir();
    const items = within(panel).getAllByRole("listitem");
    expect(items).toHaveLength(2);
    expect(items[0]).toHaveTextContent("Diagnóstico terminado");
    expect(items[0]).toHaveTextContent("El lote «Lote flota norte» terminó.");
    expect(items[0]).toHaveTextContent("12/11/2026");
    expect(within(items[0]).getByRole("link", { name: "Ver lote" })).toHaveAttribute("href", `/diagnosticos/${LOTE}`);
  });

  it("abrir un aviso lo marca leído (POST …/leida) y «Marcar todas como leídas» llama POST …/leer-todas", async () => {
    const a1 = aviso(1);
    const a2 = aviso(2);
    const api = simularApi({
      [`GET ${RUTA}`]: bandejas(bandeja([a1, a2])),
      [`POST ${RUTA}/${a1.id}/leida`]: () => new Response(null, { status: 204 }),
      [`POST ${RUTA}/leer-todas`]: () => new Response(null, { status: 204 }),
    });
    render(<CampanaDiagnostico />);
    await screen.findByRole("button", { name: "Avisos, 2 avisos sin leer" });
    const panel = await abrir();
    fireEvent.click(within(panel).getAllByRole("link", { name: "Ver lote" })[0]);
    await screen.findByRole("button", { name: "Avisos, 1 aviso sin leer" });
    expect(pedidos(api, `${RUTA}/${a1.id}/leida`)).toHaveLength(1);
    fireEvent.click(await abrir().then((p) => within(p).getByRole("button", { name: "Marcar todas como leídas" })));
    await screen.findByRole("button", { name: "Avisos" });
    expect(pedidos(api, `${RUTA}/leer-todas`)).toHaveLength(1);
  });

  it("Escape cierra el panel y devuelve el foco al botón", async () => {
    simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([])) });
    render(<CampanaDiagnostico />);
    const panel = await abrir();
    fireEvent.keyDown(panel, { key: "Escape" });
    expect(screen.queryByRole("region", { name: "Avisos" })).toBeNull();
    expect(screen.getByRole("button", { name: "Avisos" })).toHaveFocus();
  });
});

describe("HU14562 AC52 notificación persistente del ZIP", () => {
  it("un ZIP listo nuevo que llega en el sondeo abre el aviso sin autocierre; «Descargar» baja el ZIP y lo marca leído", async () => {
    const zip = aviso(9, { tipo: "ZIP_EVIDENCIAS_LISTO", referencia_id: EVIDENCIA });
    const api = simularApi({
      [`GET ${RUTA}`]: bandejas(bandeja([]), bandeja([zip])),
      [`POST ${RUTA}/${zip.id}/leida`]: () => new Response(null, { status: 204 }),
    });
    render(<CampanaDiagnostico />);
    await waitFor(() => expect(screen.getByRole("button", { name: "Avisos" })).toHaveAttribute("data-cargado", "true"));
    expect(screen.queryByRole("status")).toBeNull();
    act(() => pedirSondeoNotificaciones());
    const aviso1 = await screen.findByRole("status");
    expect(aviso1).toHaveTextContent("ZIP de evidencias listo");
    fireEvent.click(within(aviso1).getByRole("button", { name: "Descargar" }));
    await waitFor(() => expect(h.descargar).toHaveBeenCalledWith(`/api/v1/diagnostico/diagnosticos/${LOTE}/evidencias/${EVIDENCIA}/archivo`, "evidencias.zip"));
    await waitFor(() => expect(pedidos(api, `${RUTA}/${zip.id}/leida`)).toHaveLength(1));
    await waitFor(() => expect(screen.queryByRole("status")).toBeNull());
  });

  it("sin referencia_id (forma real del API) «Descargar» pide el ZIP por POST …/evidencias y baja el que ya está listo", async () => {
    const zip = aviso(9, { tipo: "ZIP_EVIDENCIAS_LISTO", referencia_id: undefined });
    const api = simularApi({
      [`GET ${RUTA}`]: bandejas(bandeja([]), bandeja([zip])),
      [`POST /diagnosticos/${LOTE}/evidencias`]: () => Response.json({ id: EVIDENCIA, tipo: "ZIP", estado: "LISTA", vehiculos_incluidos: 2, vehiculos_pendientes: 0, creado_en: "2026-11-12T15:00:00+00:00", generado_en: "2026-11-12T15:01:00+00:00", tipo_error: null }),
      [`POST ${RUTA}/${zip.id}/leida`]: () => new Response(null, { status: 204 }),
    });
    render(<CampanaDiagnostico />);
    await waitFor(() => expect(screen.getByRole("button", { name: "Avisos" })).toHaveAttribute("data-cargado", "true"));
    act(() => pedirSondeoNotificaciones());
    fireEvent.click(within(await screen.findByRole("status")).getByRole("button", { name: "Descargar" }));
    await waitFor(() => expect(h.descargar).toHaveBeenCalledWith(`/api/v1/diagnostico/diagnosticos/${LOTE}/evidencias/${EVIDENCIA}/archivo`, "evidencias.zip"));
    await waitFor(() => expect(pedidos(api, `${RUTA}/${zip.id}/leida`)).toHaveLength(1));
  });

  it("los ZIP no leídos que ya estaban al cargar no abren el aviso (solo están en la campana)", async () => {
    simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([aviso(9, { tipo: "ZIP_EVIDENCIAS_LISTO" })])) });
    render(<CampanaDiagnostico />);
    await screen.findByRole("button", { name: "Avisos, 1 aviso sin leer" });
    expect(screen.queryByRole("status")).toBeNull();
  });
});

describe("HU14562 AC53 sin PII", () => {
  it("el panel no muestra lo que venga como texto del servidor ni datos en texto", async () => {
    simularApi({
      [`GET ${RUTA}`]: bandejas(
        bandeja([{ ...aviso(1, { tipo: "DERIVACION_TERMINADA", datos: { creadas: 2, placa: "ABC123" } }), titulo: "Placa ABC123", mensaje: "CC 1020304050" } as never]),
      ),
    });
    render(<CampanaDiagnostico />);
    const panel = await abrir();
    await within(panel).findByText("Creación de trámites terminada");
    expect(panel).not.toHaveTextContent(/ABC123|1020304050/);
  });
});

describe("HU14562 AC54 campana vacía o con error", () => {
  it("vacía: muestra el estado vacío", async () => {
    simularApi({ [`GET ${RUTA}`]: bandejas(bandeja([])) });
    render(<CampanaDiagnostico />);
    const panel = await abrir();
    expect(await within(panel).findByText("No tienes avisos")).toBeInTheDocument();
  });

  it("con error: «Reintentar» vuelve a pedir y luego muestra la lista", async () => {
    let falla = true;
    const api = simularApi({ [`GET ${RUTA}`]: () => (falla ? new Response(null, { status: 500 }) : Response.json(bandeja([aviso(1)]))) });
    render(<CampanaDiagnostico />);
    const panel = await abrir();
    expect(await within(panel).findByText("No se pudieron cargar los avisos")).toBeInTheDocument();
    falla = false;
    fireEvent.click(within(panel).getByRole("button", { name: "Reintentar" }));
    expect(await within(panel).findByText("Diagnóstico terminado")).toBeInTheDocument();
    expect(pedidos(api, RUTA)).toHaveLength(2);
  });
});
