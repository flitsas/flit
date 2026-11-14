import { readFileSync } from "node:fs";
import { join } from "node:path";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import {
  PanelDeudaSaneamiento,
  type ContextoBandera,
  type PanelDeudaSaneamientoProps,
} from "@/components/saneamiento/PanelDeudaSaneamiento";
import { banderaPanelDeuda } from "@/components/saneamiento/BanderaPanelDeuda";
import type { EstadoSaneamientoActivo } from "@/lib/saneamiento/api";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";
import { LOTE, PLACA, RUTA_DEUDA, RUTA_SANEAMIENTO, VEHICULO, deudaCompleta, deudaSinPendientes, saneamientoEnGestion } from "./datosDeuda";

// Uso de ejemplo: desde la bandera, «Ver deuda», el aviso «Vehículo bloqueado» del dictamen o la grilla de placas:
//   <PanelDeudaSaneamiento diagnosticoId={id} vehiculoId={vid} identificador="ABC123" puedeSanear puedeDescargar
//     onClose={cerrar} devolverFocoA={boton} accionBandera={(ctx) => <BotonSanear … onCambio={ctx.onCambio} />} />
// Pide en paralelo GET …/deuda y GET …/saneamiento; cuatro estados; descargas PDF y Excel con useDescarga.

const h = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: h.replace, push: vi.fn(), refresh: vi.fn() }), usePathname: () => "/diagnosticos/x" }));

const json = (cuerpo: unknown, status = 200) => Response.json(cuerpo, { status });
const sinSaneamiento = () => json({ code: "SIN_SANEAMIENTO", error: "Sin saneamiento" }, 404);

/** Bandera de prueba en lugar de BotonSanear (FE-A): alterna SOLICITADO ↔ sin saneamiento como lo haría la API. */
function BanderaPrueba({ ctx }: { ctx: ContextoBandera }) {
  const siguiente: EstadoSaneamientoActivo | null = ctx.saneamiento?.estado === "SOLICITADO" ? null : "SOLICITADO";
  return (
    <button
      type="button"
      aria-pressed={ctx.saneamiento?.estado === "SOLICITADO"}
      onClick={() => {
        ctx.onCambio(siguiente);
        ctx.onCambioSaneamiento(siguiente);
      }}
    >
      Bandera de prueba
    </button>
  );
}

const pintar = (props: Partial<PanelDeudaSaneamientoProps> = {}) =>
  render(
    <PanelDeudaSaneamiento
      diagnosticoId={LOTE}
      vehiculoId={VEHICULO}
      identificador={PLACA}
      puedeSanear
      puedeDescargar
      onClose={() => {}}
      {...props}
    />,
  );

const region = () => document.querySelector("[data-region-viva]") as HTMLElement;
const seccionSaneamiento = () => screen.queryByRole("region", { name: "Saneamiento" });

let descargados: string[];
beforeEach(() => {
  h.replace.mockReset();
  descargados = [];
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    descargados.push(this.download);
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("PanelDeudaSaneamiento — AC13: panel accesible", () => {
  it("abre un diálogo «Deuda y saneamiento» con la placa, el foco dentro, y al cerrar el foco vuelve al control que lo abrió", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()), [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()) });
    function Pantalla() {
      const [abierto, setAbierto] = useState(false);
      const [boton, setBoton] = useState<HTMLButtonElement | null>(null);
      return (
        <>
          <button ref={setBoton} type="button" onClick={() => setAbierto(true)}>
            Ver deuda de la placa TST001
          </button>
          {abierto && (
            <PanelDeudaSaneamiento
              diagnosticoId={LOTE}
              vehiculoId={VEHICULO}
              identificador={PLACA}
              puedeSanear
              puedeDescargar
              onClose={() => setAbierto(false)}
              devolverFocoA={boton}
            />
          )}
        </>
      );
    }
    render(<Pantalla />);
    const abridor = screen.getByRole("button", { name: "Ver deuda de la placa TST001" });
    abridor.focus();
    fireEvent.click(abridor);
    const dialogo = screen.getByRole("dialog", { name: "Deuda y saneamiento" });
    expect(dialogo).toHaveAttribute("aria-modal", "true");
    expect(dialogo).toHaveTextContent(PLACA);
    await waitFor(() => expect(dialogo.contains(document.activeElement)).toBe(true));
    await screen.findByRole("heading", { name: "Deuda por fuente" });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Cerrar" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(document.activeElement).toBe(abridor);
  });

  it("monta la región viva vacía desde el principio", () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => new Promise<Response>(() => {}), [`GET ${RUTA_SANEAMIENTO}`]: () => new Promise<Response>(() => {}) });
    pintar();
    expect(region()).toHaveAttribute("role", "status");
    expect(region()).toHaveAttribute("aria-live", "polite");
    expect(region()).toHaveTextContent("");
  });
});

describe("PanelDeudaSaneamiento — cuatro estados", () => {
  it("cargando: pide deuda y saneamiento en paralelo y muestra «Cargando deuda…»", async () => {
    let soltarDeuda: (r: Response) => void = () => {};
    let soltarSan: (r: Response) => void = () => {};
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => new Promise<Response>((r) => (soltarDeuda = r)),
      [`GET ${RUTA_SANEAMIENTO}`]: () => new Promise<Response>((r) => (soltarSan = r)),
    });
    pintar();
    const cargando = screen.getByRole("status", { name: /Cargando deuda/ });
    expect(cargando).toHaveAttribute("aria-busy", "true");
    // Las dos lecturas salen antes de que responda ninguna.
    await waitFor(() => {
      expect(llamadas(fetchMock, "GET", RUTA_DEUDA)).toHaveLength(1);
      expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(1);
    });
    await act(async () => {
      soltarDeuda(json(deudaCompleta()));
      soltarSan(json(saneamientoEnGestion()));
    });
    expect(await screen.findByRole("heading", { name: "Deuda por fuente" })).toBeInTheDocument();
    expect(screen.queryByText("Cargando deuda…")).not.toBeInTheDocument();
  });

  it("error: «No pudimos cargar la deuda» con Reintentar, y la segunda lectura llena el panel", async () => {
    let veces = 0;
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => {
        veces += 1;
        return veces === 1 ? json({ error: "falló" }, 500) : json(deudaCompleta());
      },
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()),
    });
    pintar();
    expect(await screen.findByText("No pudimos cargar la deuda")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("heading", { name: "Deuda por fuente" })).toBeInTheDocument();
    expect(veces).toBe(2);
  });

  it("lleno sin hallazgos: «Sin pendientes», nunca vacío", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaSinPendientes()), [`GET ${RUTA_SANEAMIENTO}`]: sinSaneamiento });
    pintar();
    expect(await screen.findByText("No encontramos deudas ni pendientes en las fuentes consultadas.")).toBeInTheDocument();
    expect(seccionSaneamiento()).not.toBeInTheDocument();
  });

  it("403: lleva a /403 con el código", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json({ code: "SIN_PERMISO_DIAGNOSTICO", error: "No" }, 403),
      [`GET ${RUTA_SANEAMIENTO}`]: sinSaneamiento,
    });
    pintar();
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_DIAGNOSTICO"));
  });
});

describe("PanelDeudaSaneamiento — AC19: vehículo no terminado", () => {
  it("409 VEHICULO_NO_TERMINADO: aviso que explica que faltan consultas, sin código técnico, sin reintentar ni descargas", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json({ code: "VEHICULO_NO_TERMINADO", error: "VEHICULO_NO_TERMINADO" }, 409),
      [`GET ${RUTA_SANEAMIENTO}`]: sinSaneamiento,
    });
    pintar({ accionBandera: (ctx) => <BanderaPrueba ctx={ctx} /> });
    expect(await screen.findByText("El vehículo aún no termina")).toBeInTheDocument();
    expect(screen.getByText("Cuando terminen sus consultas verás aquí la deuda por fuente.")).toBeInTheDocument();
    const dialogo = screen.getByRole("dialog");
    expect(dialogo).not.toHaveTextContent("VEHICULO_NO_TERMINADO");
    expect(dialogo).not.toHaveTextContent("409");
    expect(screen.queryByRole("button", { name: "Reintentar" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Descargar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Bandera de prueba" })).not.toBeInTheDocument();
  });
});

describe("PanelDeudaSaneamiento — AC14: encabezado", () => {
  it("muestra la placa, la etiqueta del estado y la bandera con el estado del saneamiento", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()), [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()) });
    const recibido: ContextoBandera[] = [];
    pintar({
      accionBandera: (ctx) => {
        recibido.push(ctx);
        return <BanderaPrueba ctx={ctx} />;
      },
    });
    const estado = await screen.findByRole("region", { name: "Estado" });
    expect(within(estado).getByText(PLACA)).toBeInTheDocument();
    expect(within(estado).getByText("En saneamiento")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bandera de prueba" })).toBeInTheDocument();
    expect(recibido.at(-1)?.saneamiento).toEqual({ estado: "EN_GESTION", elegible: false, motivo_no_elegible: "YA_SOLICITADO" });
    expect(recibido.at(-1)?.puedeSanear).toBe(true);
    expect(screen.getByRole("heading", { level: 3, name: "Dictamen" })).toBeInTheDocument();
  });

  it("sin saneamiento y con la capacidad de sanear: ayuda para marcar la bandera; sin la capacidad, no", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaSinPendientes()), [`GET ${RUTA_SANEAMIENTO}`]: sinSaneamiento });
    const { unmount } = pintar();
    expect(await screen.findByText("Marca la bandera para pedir a FLIT que gestione el saneamiento.")).toBeInTheDocument();
    unmount();
    pintar({ puedeSanear: false });
    await screen.findByRole("heading", { name: "Deuda por fuente" });
    expect(screen.queryByText("Marca la bandera para pedir a FLIT que gestione el saneamiento.")).not.toBeInTheDocument();
  });
});

describe("PanelDeudaSaneamiento — AC16: saneamiento para el cliente", () => {
  it("estado, fechas, observación, renglones con «Descargar evidencia» e historial reducido con actores sin identificar", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()), [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()) });
    pintar();
    const seccion = await screen.findByRole("region", { name: "Saneamiento" });
    expect(seccion).toHaveTextContent("Solicitado04/11/2026");
    expect(seccion).toHaveTextContent("PorTú");
    expect(seccion).toHaveTextContent("Tomado por FLIT05/11/2026");
    expect(seccion).toHaveTextContent("Cerrado—");
    expect(within(seccion).getByText("Observación de FLIT")).toBeInTheDocument();
    expect(within(seccion).getByText("Se gestiona el pago con el organismo de tránsito.")).toBeInTheDocument();

    const tabla = within(seccion).getByRole("table", { name: "Conceptos del saneamiento" });
    expect(within(tabla).getAllByRole("columnheader").map((c) => c.textContent)).toEqual(["Concepto", "Hallazgo", "Resultado", "Nota", "Evidencia"]);
    const filas = within(tabla).getAllByRole("row").slice(1);
    expect(filas[0]).toHaveTextContent("SIMIT del propietario");
    expect(filas[0]).toHaveTextContent("Con deuda");
    expect(filas[0]).toHaveTextContent("Saneado");
    expect(filas[0]).toHaveTextContent("Pagado en el portal.");
    const evidencia = within(filas[0]).getByRole("link", { name: "Descargar evidencia de SIMIT del propietario" });
    expect(evidencia).toHaveTextContent("Descargar evidencia");
    expect(evidencia).toHaveAttribute("href", `/api/v1/diagnostico${RUTA_SANEAMIENTO}/renglones/ren-1/evidencia`);
    expect(filas[1]).toHaveTextContent("Otro: Embargo registrado");
    expect(filas[1]).toHaveTextContent("Pendiente");
    expect(within(filas[1]).queryByRole("link")).not.toBeInTheDocument();

    const eventos = within(within(seccion).getByRole("list", { name: "Historial del saneamiento" })).getAllByRole("listitem");
    expect(eventos).toHaveLength(3);
    expect(eventos[0]).toHaveTextContent("Se solicitó el saneamiento");
    // UA-8: el autor del SOLICITADO sale de solicitado_por_mi.
    expect(eventos[0]).toHaveTextContent("Tú");
    expect(eventos[0]).not.toHaveTextContent("Tu compañía");
    expect(eventos[1]).toHaveTextContent("FLIT empezó el saneamiento");
    expect(eventos[1]).toHaveTextContent("FLIT");
    expect(eventos[2]).toHaveTextContent("FLIT cargó una evidencia");
    // Nunca un id de usuario, de saneamiento o de renglón.
    expect(seccion).not.toHaveTextContent(/san-1|ren-1|usuario/);
  });

  it("solicitado por otro usuario: «Otro usuario de la compañía»", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()),
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion({ solicitado_por_mi: false })),
    });
    pintar();
    const seccion = await screen.findByRole("region", { name: "Saneamiento" });
    expect(seccion).toHaveTextContent("PorOtro usuario de la compañía");
    // UA-8: el evento SOLICITADO del historial lleva el mismo actor.
    const [solicitado] = within(within(seccion).getByRole("list", { name: "Historial del saneamiento" })).getAllByRole("listitem");
    expect(solicitado).toHaveTextContent("Se solicitó el saneamiento");
    expect(solicitado).toHaveTextContent("Otro usuario de la compañía");
  });

  it("UA-8: DESMARCADO sigue rotulado «Tu compañía» (no se sabe quién fue, SP-15)", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()),
      [`GET ${RUTA_SANEAMIENTO}`]: () =>
        json(saneamientoEnGestion({ historial: [{ evento: "DESMARCADO", ocurrido_en: "2026-11-06T12:00:00+00:00", actor: "COMPANIA", fuente: null, motivo: null }] })),
    });
    pintar();
    const seccion = await screen.findByRole("region", { name: "Saneamiento" });
    const [evento] = within(within(seccion).getByRole("list", { name: "Historial del saneamiento" })).getAllByRole("listitem");
    expect(evento).toHaveTextContent("Tu compañía");
  });

  it("NO_PROCEDE muestra el motivo del cierre; un REABIERTO muestra su motivo en el historial", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta({ saneamiento: { estado: "NO_PROCEDE" } })),
      [`GET ${RUTA_SANEAMIENTO}`]: () =>
        json(
          saneamientoEnGestion({
            estado: "NO_PROCEDE",
            cerrado_en: "2026-11-08T12:00:00+00:00",
            motivo_cierre: "La deuda corresponde a otro vehículo.",
            historial: [
              { evento: "REABIERTO", ocurrido_en: "2026-11-07T12:00:00+00:00", actor: "FLIT", fuente: null, motivo: "Faltaba revisar el SOAT." },
              { evento: "CERRADO_NO_PROCEDE", ocurrido_en: "2026-11-08T12:00:00+00:00", actor: "FLIT", fuente: null, motivo: null },
            ],
          }),
        ),
    });
    pintar();
    const seccion = await screen.findByRole("region", { name: "Saneamiento" });
    expect(seccion).toHaveTextContent("Cerrado08/11/2026");
    expect(within(seccion).getByText("Motivo del cierre")).toBeInTheDocument();
    expect(within(seccion).getByText("La deuda corresponde a otro vehículo.")).toBeInTheDocument();
    const eventos = within(within(seccion).getByRole("list", { name: "Historial del saneamiento" })).getAllByRole("listitem");
    expect(eventos[0]).toHaveTextContent("FLIT reabrió el saneamiento");
    expect(eventos[0]).toHaveTextContent("Motivo: Faltaba revisar el SOAT.");
    expect(eventos[1]).toHaveTextContent("FLIT cerró el saneamiento: no procede");
    expect(within(screen.getByRole("region", { name: "Estado" })).getByText("Saneamiento no procede")).toBeInTheDocument();
  });

  it("ANULADO por FLIT: muestra el motivo de la anulación, sin etiqueta de estado", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta({ saneamiento: null })),
      [`GET ${RUTA_SANEAMIENTO}`]: () =>
        json(saneamientoEnGestion({ estado: "ANULADO", motivo_anulacion: "La compañía pidió cancelar la gestión.", renglones: [] })),
    });
    pintar();
    const seccion = await screen.findByRole("region", { name: "Saneamiento" });
    expect(within(seccion).getByText("Motivo de la anulación")).toBeInTheDocument();
    expect(within(seccion).getByText("La compañía pidió cancelar la gestión.")).toBeInTheDocument();
    expect(within(seccion).getByText("FLIT aún no registra conceptos.")).toBeInTheDocument();
    expect(screen.queryByText("Anulado")).not.toBeInTheDocument();
  });

  it("si falla el detalle del saneamiento, la sección muestra su error con Reintentar y el resto del panel sigue", async () => {
    let veces = 0;
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()),
      [`GET ${RUTA_SANEAMIENTO}`]: () => {
        veces += 1;
        return veces === 1 ? json({ error: "falló" }, 500) : json(saneamientoEnGestion());
      },
    });
    pintar();
    expect(await screen.findByText("No pudimos cargar el saneamiento")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Deuda por fuente" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("table", { name: "Conceptos del saneamiento" })).toBeInTheDocument();
    expect(llamadas(fetchMock, "GET", RUTA_DEUDA)).toHaveLength(1);
    expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(2);
  });
});

describe("PanelDeudaSaneamiento — AC17: saneado pero bloqueado", () => {
  const TEXTO = "El saneamiento está registrado, pero el dictamen no cambia solo. Se actualiza cuando se vuelvan a consultar las fuentes o FLIT lo ajuste.";

  it("SANEADO con veredicto BLOQUEADO: texto fijo «El dictamen no cambia solo»", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta({ saneamiento: { estado: "SANEADO" } })),
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion({ estado: "SANEADO", cerrado_en: "2026-11-08T12:00:00+00:00" })),
    });
    pintar();
    expect(await screen.findByText("El dictamen no cambia solo")).toBeInTheDocument();
    expect(screen.getByText(TEXTO)).toBeInTheDocument();
  });

  it("SANEADO y VIABLE: el texto no aparece", async () => {
    const deuda = deudaCompleta({ saneamiento: { estado: "SANEADO" } });
    deuda.dictamen = { ...deuda.dictamen!, veredicto: "VIABLE" };
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deuda),
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion({ estado: "SANEADO" })),
    });
    pintar();
    await screen.findByRole("region", { name: "Saneamiento" });
    expect(screen.queryByText(TEXTO)).not.toBeInTheDocument();
  });
});

describe("PanelDeudaSaneamiento — AC18: descargas", () => {
  it("«Descargar PDF» y «Descargar Excel» van por useDescarga a las rutas del panel, con estado cargando hasta terminar", async () => {
    let soltar: (r: Response) => void = () => {};
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()),
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()),
      [`GET ${RUTA_DEUDA}/pdf`]: () => new Promise<Response>((r) => (soltar = r)),
      [`GET ${RUTA_DEUDA}/excel`]: () => new Response(new Blob(["x"]), { headers: { "Content-Disposition": 'attachment; filename="deuda-TST001.xlsx"' } }),
    });
    pintar();
    const pdf = await screen.findByRole("link", { name: "Descargar PDF" });
    const excel = screen.getByRole("link", { name: "Descargar Excel" });
    expect(pdf).toHaveAttribute("href", `/api/v1/diagnostico${RUTA_DEUDA}/pdf`);
    expect(excel).toHaveAttribute("href", `/api/v1/diagnostico${RUTA_DEUDA}/excel`);
    fireEvent.click(pdf);
    await waitFor(() => expect(pdf).toHaveAttribute("aria-busy", "true"));
    expect(pdf).toHaveTextContent("Descargando…");
    await act(async () => {
      soltar(new Response(new Blob(["%PDF"]), { headers: { "Content-Disposition": 'attachment; filename="deuda-TST001.pdf"' } }));
    });
    await waitFor(() => expect(pdf).not.toHaveAttribute("aria-busy"));
    expect(pdf).toHaveTextContent("Descargar PDF");
    fireEvent.click(excel);
    await waitFor(() => expect(descargados).toEqual(["deuda-TST001.pdf", "deuda-TST001.xlsx"]));
    expect(llamadas(fetchMock, "GET", `${RUTA_DEUDA}/pdf`)).toHaveLength(1);
  });

  it("N-3: sin placa y sin Content-Disposition, el nombre por defecto es deuda-sin-placa (como el servidor), no la etiqueta «Sin placa»", async () => {
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta({ vehiculo: { id: VEHICULO, placa: null } })),
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()),
      [`GET ${RUTA_DEUDA}/pdf`]: () => new Response(new Blob(["%PDF"])),
      [`GET ${RUTA_DEUDA}/excel`]: () => new Response(new Blob(["x"])),
    });
    pintar({ identificador: "Sin placa" });
    const pdf = await screen.findByRole("link", { name: "Descargar PDF" });
    fireEvent.click(pdf);
    await waitFor(() => expect(descargados).toEqual(["deuda-sin-placa.pdf"]));
    fireEvent.click(screen.getByRole("link", { name: "Descargar Excel" }));
    await waitFor(() => expect(descargados).toEqual(["deuda-sin-placa.pdf", "deuda-sin-placa.xlsx"]));
    // El texto visible no cambia.
    expect(screen.getByRole("dialog", { name: "Deuda y saneamiento" })).toHaveTextContent("Sin placa");
  });

  it("si la descarga falla: mensaje en español y se puede reintentar", async () => {
    let veces = 0;
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()),
      [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()),
      [`GET ${RUTA_DEUDA}/pdf`]: () => {
        veces += 1;
        return veces === 1 ? json({ code: "ERROR_INTERNO", error: "boom" }, 500) : new Response(new Blob(["%PDF"]));
      },
    });
    pintar();
    const pdf = await screen.findByRole("link", { name: "Descargar PDF" });
    fireEvent.click(pdf);
    expect(await screen.findByText("No pudimos generar el archivo. Intenta de nuevo.")).toHaveAttribute("role", "alert");
    expect(screen.queryByText("boom")).not.toBeInTheDocument();
    fireEvent.click(pdf);
    await waitFor(() => expect(veces).toBe(2));
    await waitFor(() => expect(screen.queryByText("No pudimos generar el archivo. Intenta de nuevo.")).not.toBeInTheDocument());
  });

  it("sin descargarReportes: ni botones de descarga ni «Descargar evidencia» (solo «Con evidencia»)", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()), [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()) });
    pintar({ puedeDescargar: false });
    const seccion = await screen.findByRole("region", { name: "Saneamiento" });
    expect(screen.queryByRole("link", { name: /Descargar/ })).not.toBeInTheDocument();
    expect(within(seccion).getByText("Con evidencia")).toBeInTheDocument();
  });
});

describe("PanelDeudaSaneamiento — §3.7: tras marcar o desmarcar", () => {
  it("desmarcar: recarga solo el saneamiento, la sección desaparece, se anuncia y el foco sigue en la bandera", async () => {
    let estado: "SOLICITADO" | null = "SOLICITADO";
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta({ saneamiento: { estado: "SOLICITADO" } })),
      [`GET ${RUTA_SANEAMIENTO}`]: () =>
        estado ? json(saneamientoEnGestion({ estado: "SOLICITADO", tomado_en: null, renglones: [], observacion_flit: null })) : sinSaneamiento(),
    });
    const onCambioEstado = vi.fn();
    pintar({ accionBandera: (ctx) => <BanderaPrueba ctx={ctx} />, onCambioEstado });
    await screen.findByRole("region", { name: "Saneamiento" });
    const bandera = screen.getByRole("button", { name: "Bandera de prueba" });
    expect(bandera).toHaveAttribute("aria-pressed", "true");
    bandera.focus();
    estado = null;
    fireEvent.click(bandera);
    await waitFor(() => expect(seccionSaneamiento()).not.toBeInTheDocument());
    expect(llamadas(fetchMock, "GET", RUTA_DEUDA)).toHaveLength(1);
    expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(2);
    await waitFor(() => expect(region()).toHaveTextContent("Se quitó la solicitud de saneamiento de TST001."));
    expect(document.activeElement).toBe(screen.getByRole("button", { name: "Bandera de prueba" }));
    expect(screen.getByRole("button", { name: "Bandera de prueba" })).toHaveAttribute("aria-pressed", "false");
    expect(onCambioEstado).toHaveBeenCalledWith(null);
    expect(screen.getByText("Marca la bandera para pedir a FLIT que gestione el saneamiento.")).toBeInTheDocument();
  });

  it("marcar: muestra la etiqueta «Saneamiento solicitado», recarga el detalle y lo anuncia", async () => {
    let estado: "SOLICITADO" | null = null;
    simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaSinPendientes()),
      [`GET ${RUTA_SANEAMIENTO}`]: () =>
        estado ? json(saneamientoEnGestion({ estado: "SOLICITADO", tomado_en: null, renglones: [], observacion_flit: null, historial: [] })) : sinSaneamiento(),
    });
    const onCambioEstado = vi.fn();
    pintar({ accionBandera: (ctx) => <BanderaPrueba ctx={ctx} />, onCambioEstado });
    const bandera = await screen.findByRole("button", { name: "Bandera de prueba" });
    estado = "SOLICITADO";
    fireEvent.click(bandera);
    expect(within(screen.getByRole("region", { name: "Estado" })).getByText("Saneamiento solicitado")).toBeInTheDocument();
    expect(await screen.findByRole("region", { name: "Saneamiento" })).toBeInTheDocument();
    await waitFor(() => expect(region()).toHaveTextContent("Saneamiento solicitado para TST001."));
    expect(onCambioEstado).toHaveBeenCalledWith("SOLICITADO");
  });
});

// Uso de ejemplo (HU #14577 FE-C, cableado-fe-c.md §1 y §3): quien pinta el panel monta la bandera real de la cabecera con
//   accionBandera={banderaPanelDeuda(diagnosticoId, { id: vehiculoId, placa })}
// que es BotonSanear variante «cabecera»: el optimista va por ctx.onCambio y la recarga por ctx.onCambioSaneamiento solo
// cuando la API confirma. La etiqueta de la sección «Estado» es EtiquetaSaneamiento.
describe("PanelDeudaSaneamiento — FE-C: bandera real en la cabecera y etiqueta común", () => {
  const PUT_SAN = `PUT ${RUTA_SANEAMIENTO}`;
  const DELETE_SAN = `DELETE ${RUTA_SANEAMIENTO}`;
  const solicitadoApi = () => json(saneamientoEnGestion({ estado: "SOLICITADO", tomado_en: null, renglones: [], historial: [], observacion_flit: null }));
  const bandera = () => screen.getByRole("button", { name: `Solicitar saneamiento de la placa ${PLACA}` });

  it("marcar: optimista al clic, sin releer el detalle mientras el PUT está en vuelo; al confirmar relee una vez y anuncia una sola vez", async () => {
    let soltar: (r: Response) => void = () => {};
    let marcado = false;
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaSinPendientes()),
      [`GET ${RUTA_SANEAMIENTO}`]: () => (marcado ? solicitadoApi() : sinSaneamiento()),
      [PUT_SAN]: () => new Promise<Response>((r) => (soltar = r)),
    });
    const onCambioEstado = vi.fn();
    pintar({ accionBandera: banderaPanelDeuda(LOTE, { id: VEHICULO, placa: PLACA }), onCambioEstado });
    await screen.findByRole("heading", { name: "Deuda por fuente" });
    expect(bandera()).toHaveAttribute("aria-pressed", "false");
    expect(bandera().className).toContain("var(--kit-tinta)");
    bandera().focus();
    fireEvent.click(bandera());
    expect(bandera()).toHaveAttribute("aria-pressed", "true");
    expect(within(screen.getByRole("region", { name: "Estado" })).getByText("Saneamiento solicitado")).toBeInTheDocument();
    expect(onCambioEstado).toHaveBeenCalledWith("SOLICITADO");
    expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(1);
    await act(async () => {
      marcado = true;
      soltar(json({ id: "san-1", estado: "SOLICITADO" }, 201));
      await new Promise((r) => setTimeout(r, 0));
    });
    await waitFor(() => expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(2));
    expect(llamadas(fetchMock, "GET", RUTA_DEUDA)).toHaveLength(1);
    await waitFor(() => expect(region()).toHaveTextContent("Saneamiento solicitado para TST001."));
    expect(region().querySelectorAll("span")).toHaveLength(1);
    expect(bandera()).toHaveFocus();
    expect(await screen.findByRole("region", { name: "Saneamiento" })).toBeInTheDocument();
  });

  it("desmarcar con DELETE 204: la sección se va, se relee el detalle y se anuncia; el foco sigue en la bandera", async () => {
    let marcado = true;
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta({ saneamiento: { estado: "SOLICITADO" } })),
      [`GET ${RUTA_SANEAMIENTO}`]: () => (marcado ? solicitadoApi() : sinSaneamiento()),
      [DELETE_SAN]: () => {
        marcado = false;
        return new Response(null, { status: 204 });
      },
    });
    pintar({ accionBandera: banderaPanelDeuda(LOTE, { id: VEHICULO, placa: PLACA }) });
    await screen.findByRole("region", { name: "Saneamiento" });
    expect(bandera()).toHaveAttribute("aria-pressed", "true");
    bandera().focus();
    fireEvent.click(bandera());
    await waitFor(() => expect(region()).toHaveTextContent("Se quitó la solicitud de saneamiento de TST001."));
    expect(seccionSaneamiento()).not.toBeInTheDocument();
    expect(llamadas(fetchMock, "DELETE", RUTA_SANEAMIENTO)).toHaveLength(1);
    expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(2);
    expect(bandera()).toHaveFocus();
  });

  it("si el PUT falla: revierte la etiqueta, el error va junto a la bandera, no relee ni anuncia", async () => {
    const fetchMock = simularApi({
      [`GET ${RUTA_DEUDA}`]: () => json(deudaSinPendientes()),
      [`GET ${RUTA_SANEAMIENTO}`]: sinSaneamiento,
      [PUT_SAN]: () => json({ code: "VEHICULO_NO_TERMINADO", error: "x" }, 409),
    });
    pintar({ accionBandera: banderaPanelDeuda(LOTE, { id: VEHICULO, placa: PLACA }) });
    await screen.findByRole("heading", { name: "Deuda por fuente" });
    fireEvent.click(bandera());
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("Este vehículo aún tiene consultas por terminar."));
    expect(bandera()).toHaveAttribute("aria-pressed", "false");
    expect(within(screen.getByRole("region", { name: "Estado" })).queryByText("Saneamiento solicitado")).toBeNull();
    expect(llamadas(fetchMock, "GET", RUTA_SANEAMIENTO)).toHaveLength(1);
    expect(region()).toHaveTextContent("");
  });

  it("D-101: con EN_GESTION (o sin la capacidad) la cabecera no repite «Ver saneamiento»: el estado ya está en la sección «Estado»", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()), [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()) });
    const { unmount } = pintar({ accionBandera: banderaPanelDeuda(LOTE, { id: VEHICULO, placa: PLACA }) });
    await screen.findByRole("region", { name: "Saneamiento" });
    expect(screen.queryByRole("button", { name: /saneamiento de la placa/ })).toBeNull();
    unmount();
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaSinPendientes()), [`GET ${RUTA_SANEAMIENTO}`]: sinSaneamiento });
    pintar({ puedeSanear: false, accionBandera: banderaPanelDeuda(LOTE, { id: VEHICULO, placa: PLACA }) });
    await screen.findByRole("heading", { name: "Deuda por fuente" });
    expect(screen.queryByRole("button", { name: /saneamiento de la placa/ })).toBeNull();
  });

  it("la sección «Estado» pinta la etiqueta con EtiquetaSaneamiento (fuente única de texto y tono)", async () => {
    simularApi({ [`GET ${RUTA_DEUDA}`]: () => json(deudaCompleta()), [`GET ${RUTA_SANEAMIENTO}`]: () => json(saneamientoEnGestion()) });
    pintar();
    const estado = await screen.findByRole("region", { name: "Estado" });
    const etiqueta = within(estado).getByText("En saneamiento");
    expect(etiqueta.style.getPropertyValue("--ct")).toBe("var(--badge-warning-fg)");
    const fuente = readFileSync(join(__dirname, "..", "PanelDeudaSaneamiento.tsx"), "utf8");
    expect(fuente).toMatch(/<EtiquetaSaneamiento estado=\{estado\} \/>/);
    expect(fuente).not.toMatch(/etiqueta && <EtiquetaTono/);
  });
});
