import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import PaginaGestionSaneamiento from "@/app/(app)/backoffice/saneamientos/[sid]/page";
import { GestionSaneamiento } from "@/components/backoffice/GestionSaneamiento";
import { sesion } from "@/__tests__/configuracion/datos";
import type { SaneamientoBackoffice } from "@/lib/backoffice/saneamientos";
import { ResumenSaneamientosContext } from "@/lib/backoffice/useResumenSaneamientos";
import type { RenglonSaneamiento } from "@/lib/saneamiento/api";
import {
  EMPRESAS,
  EMPRESA_A,
  LOTE_BO,
  RID_PROPIETARIO,
  RID_SOAT,
  RUTA_DETALLE,
  SID,
  USUARIO_FLIT,
  deudaBackoffice,
  llamadas,
  pdf,
  renglon,
  renglonSoat,
  saneamientoBo,
  saneamientoSolicitado,
  simularApi,
} from "./datosBackoffice";

// Uso de ejemplo: /backoffice/saneamientos/{sid}?empresa=… monta <GestionSaneamiento sid={sid} empresa={empresa} /> dentro
// de AccesoSaneamientos. Pide en paralelo GET /backoffice/saneamientos/{sid}?empresa= (detalle con historial completo) y
// GET …/{sid}/deuda?empresa= (SeccionesDeuda en lectura). Según el estado: «Tomar», renglones editables con guardado
// automático, evidencia PDF, otro concepto, observación, cierre, reapertura, anulación e historial; un 409
// VERSION_DESACTUALIZADA abre la NotificacionPersistente con «Recargar».

const h = vi.hoisted(() => ({
  capacidades: { backoffice: true, gestionarSaneamiento: true } as Record<string, boolean>,
  replace: vi.fn(),
  push: vi.fn(),
}));
vi.mock("@/components/DiagnosticoShell", () => ({ useSesion: () => ({ sesion: sesion(h.capacidades), cargando: false, error: null, recargar: () => {} }) }));
vi.mock("next/navigation", () => ({ usePathname: () => "/backoffice/saneamientos/x", useRouter: () => ({ replace: h.replace, push: h.push, refresh: vi.fn() }) }));

type Rutas = Parameters<typeof simularApi>[0];
const json = (cuerpo: unknown, status = 200) => Response.json(cuerpo, { status });
const error = (code: string, status: number) => json({ code, error: "x" }, status);
const cuerpoDe = (llamada: unknown[]) => JSON.parse(String((llamada[1] as RequestInit).body));

function api(detalle: SaneamientoBackoffice | (() => Response), rutas: Rutas = {}) {
  return simularApi({
    "GET /backoffice/empresas": () => json(EMPRESAS),
    [`GET ${RUTA_DETALLE}`]: typeof detalle === "function" ? detalle : () => json(detalle),
    [`GET ${RUTA_DETALLE}/deuda`]: () => json(deudaBackoffice()),
    ...rutas,
  });
}

const montar = (refrescar = vi.fn()) =>
  render(
    <ResumenSaneamientosContext.Provider value={{ solicitados: 1, refrescar }}>
      <GestionSaneamiento sid={SID} empresa={EMPRESA_A} />
    </ResumenSaneamientosContext.Provider>,
  );

const listo = () => screen.findByRole("heading", { level: 1, name: "TST001" });
const gestion = () => screen.getByRole("region", { name: "Gestión" });
const region = () => document.querySelector("[data-region-viva]") as HTMLElement;
const resultado = (fila: string) => screen.getByRole("radiogroup", { name: `Resultado de ${fila}` });
const estadoFila = (fila: string) => within(screen.getByRole("row", { name: fila })).getByText(/Sin cambios|Sin guardar|Guardando…|Guardado|No se guardó/);
const dialogo = (nombre: string) => screen.findByRole("dialog", { name: nombre });

beforeEach(() => {
  h.capacidades = { backoffice: true, gestionarSaneamiento: true };
  h.replace.mockReset();
  h.push.mockReset();
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("PS-5 — página y cuatro estados (AC40)", () => {
  it("sin ?empresa= válida vuelve a la bandeja", async () => {
    render(await PaginaGestionSaneamiento({ params: Promise.resolve({ sid: SID }), searchParams: Promise.resolve({}) }));
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/backoffice/saneamientos"));
  });

  it("sin gestionarSaneamiento va a la 403 y no pide el detalle", async () => {
    h.capacidades = { backoffice: true, gestionarSaneamiento: false };
    const f = api(saneamientoBo());
    render(await PaginaGestionSaneamiento({ params: Promise.resolve({ sid: SID }), searchParams: Promise.resolve({ empresa: EMPRESA_A }) }));
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_SANEAMIENTO"));
    expect(llamadas(f, "GET", RUTA_DETALLE)).toHaveLength(0);
  });

  it("cargando: «Cargando saneamiento…»", () => {
    api(() => new Promise<Response>(() => {}) as never);
    montar();
    expect(screen.getByRole("status", { name: "Cargando saneamiento…" })).toBeInTheDocument();
  });

  it("error: «No pudimos cargar el saneamiento» con Reintentar", async () => {
    let n = 0;
    api(() => (++n === 1 ? json({ error: "x" }, 500) : json(saneamientoBo())));
    montar();
    const alerta = await screen.findByRole("alert");
    expect(alerta).toHaveTextContent("No pudimos cargar el saneamiento");
    fireEvent.click(within(alerta).getByRole("button", { name: "Reintentar" }));
    expect(await listo()).toBeInTheDocument();
  });

  it("404: «No encontramos este saneamiento» con «Volver a la bandeja»", async () => {
    api(() => error("NO_ENCONTRADO", 404));
    montar();
    expect(await screen.findByRole("heading", { name: "No encontramos este saneamiento" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Volver a la bandeja" })).toHaveAttribute("href", "/backoffice/saneamientos");
  });

  it("403 de la API lleva a la 403", async () => {
    api(() => error("SIN_PERMISO", 403));
    montar();
    await waitFor(() => expect(h.replace).toHaveBeenCalledWith("/403?code=SIN_PERMISO_SANEAMIENTO"));
  });

  it("vacío: EN_GESTION sin conceptos muestra el texto de la tabla", async () => {
    api(saneamientoBo({ renglones: [] }));
    montar();
    await listo();
    expect(screen.getByText("No hay conceptos. Agrega uno con «Agregar otro concepto» o cierra como no procede.")).toBeInTheDocument();
  });
});

describe("AC29 — encabezado y deuda en modo lectura", () => {
  it("encabezado con placa, compañía, diagnóstico enlazado al lote, estado y veredicto; miga a la bandeja", async () => {
    const f = api(saneamientoBo());
    montar();
    await listo();
    const cabecera = screen.getByRole("heading", { level: 1, name: "TST001" }).closest("header")!;
    await waitFor(() => expect(cabecera).toHaveTextContent("Transportes A SAS"));
    expect(within(cabecera).getByRole("link", { name: "Lote octubre" })).toHaveAttribute("href", `/backoffice/diagnosticos/${LOTE_BO}?empresa=${EMPRESA_A}`);
    expect(cabecera).toHaveTextContent("En saneamiento");
    expect(cabecera).toHaveTextContent("Bloqueado");
    expect(within(screen.getByRole("navigation", { name: "Miga de pan" })).getByRole("link", { name: "Saneamientos" })).toHaveAttribute("href", "/backoffice/saneamientos");
    expect(new URL(String(llamadas(f, "GET", RUTA_DETALLE)[0][0]), "http://x").searchParams.get("empresa")).toBe(EMPRESA_A);
  });

  it("la columna izquierda muestra Dictamen y Deuda por fuente leídos de GET …/{sid}/deuda, sin bandera ni descargas", async () => {
    const f = api(saneamientoBo());
    montar();
    await listo();
    const deuda = screen.getByRole("region", { name: "Deuda del vehículo" });
    expect(await within(deuda).findByRole("heading", { name: "Dictamen" })).toBeInTheDocument();
    expect(within(deuda).getAllByText(/SIMIT del propietario/).length).toBeGreaterThan(0);
    expect(new URL(String(llamadas(f, "GET", `${RUTA_DETALLE}/deuda`)[0][0]), "http://x").searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(screen.queryByRole("button", { name: /^Solicitar saneamiento/ })).toBeNull();
    expect(screen.queryByRole("link", { name: /Descargar PDF/ })).toBeNull();
  });

  it("si la deuda falla, su columna muestra el error con Reintentar y la gestión sigue a la vista", async () => {
    api(saneamientoBo(), { [`GET ${RUTA_DETALLE}/deuda`]: () => json({ error: "x" }, 500) });
    montar();
    await listo();
    const deuda = screen.getByRole("region", { name: "Deuda del vehículo" });
    expect(await within(deuda).findByRole("alert")).toHaveTextContent("No pudimos cargar la deuda");
    expect(within(gestion()).getByRole("table", { name: "Conceptos a sanear" })).toBeInTheDocument();
  });
});

describe("AC30 — Tomar y anular", () => {
  it("SOLICITADO: «Tomar» pasa a EN_GESTION, muestra los conceptos, refresca la insignia, lleva el foco al título y lo anuncia", async () => {
    const f = api(saneamientoSolicitado(), { [`POST ${RUTA_DETALLE}/tomar`]: () => json(saneamientoBo()) });
    const refrescar = vi.fn();
    montar(refrescar);
    await listo();
    expect(gestion()).toHaveTextContent("La compañía pidió el saneamiento el 03/11/2026.");
    fireEvent.click(within(gestion()).getByRole("button", { name: "Tomar" }));
    const titulo = await screen.findByRole("heading", { name: "Conceptos a sanear" });
    expect(llamadas(f, "POST", `${RUTA_DETALLE}/tomar`)).toHaveLength(1);
    expect(screen.getByRole("row", { name: "SIMIT del propietario" })).toBeInTheDocument();
    await waitFor(() => expect(titulo).toHaveFocus());
    expect(region()).toHaveTextContent("Saneamiento tomado. Se generaron 2 conceptos.");
    expect(refrescar).toHaveBeenCalled();
  });

  it("«Anular» pide el motivo de 10 a 500 caracteres, no deja confirmar fuera de rango y al confirmar queda ANULADO", async () => {
    const f = api(saneamientoSolicitado(), {
      [`POST ${RUTA_DETALLE}/anular`]: () => json(saneamientoSolicitado({ estado: "ANULADO", motivo_anulacion: "La compañía lo pidió por error." })),
    });
    montar();
    await listo();
    fireEvent.click(within(gestion()).getByRole("button", { name: "Anular" }));
    const modal = await dialogo("Anular el saneamiento");
    const motivo = within(modal).getByLabelText("Motivo");
    await waitFor(() => expect(motivo).toHaveFocus());
    expect(motivo).toHaveAttribute("maxLength", "500");
    fireEvent.change(motivo, { target: { value: "corto" } });
    fireEvent.click(within(modal).getByRole("button", { name: "Anular" }));
    expect(within(modal).getByText("Escribe al menos 10 caracteres.")).toBeInTheDocument();
    expect(motivo).toHaveAttribute("aria-invalid", "true");
    expect(llamadas(f, "POST", `${RUTA_DETALLE}/anular`)).toHaveLength(0);
    fireEvent.change(motivo, { target: { value: "La compañía lo pidió por error." } });
    fireEvent.click(within(modal).getByRole("button", { name: "Anular" }));
    expect(await screen.findByText("Saneamiento anulado", { selector: "*" })).toBeInTheDocument();
    expect(cuerpoDe(llamadas(f, "POST", `${RUTA_DETALLE}/anular`)[0])).toEqual({ motivo: "La compañía lo pidió por error.", version: 123 });
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(gestion()).toHaveTextContent("La compañía lo pidió por error.");
    expect(region()).toHaveTextContent("Saneamiento anulado.");
    await waitFor(() => expect(document.querySelector("[data-estado-saneamiento]")).toHaveFocus());
  });

  it("EN_GESTION también ofrece «Anular»", async () => {
    api(saneamientoBo());
    montar();
    await listo();
    expect(within(gestion()).getByRole("button", { name: "Anular" })).toBeInTheDocument();
  });

  it("ANULADO: solo lectura con el motivo y sin acciones", async () => {
    api(saneamientoSolicitado({ estado: "ANULADO", motivo_anulacion: "La compañía lo pidió por error." }));
    montar();
    await listo();
    expect(within(gestion()).getByText("Saneamiento anulado")).toBeInTheDocument();
    expect(gestion()).toHaveTextContent("La compañía lo pidió por error.");
    expect(within(gestion()).queryAllByRole("button")).toHaveLength(0);
  });
});

describe("AC31 — renglones con guardado uno a uno", () => {
  it("el resultado con SelectorSegmentado guarda solo ese renglón con su version y muestra Guardando… y luego Guardado", async () => {
    let soltar: (r: Response) => void = () => {};
    const f = api(saneamientoBo(), {
      [`PUT ${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}`]: () => new Promise<Response>((r) => (soltar = r)),
    });
    montar();
    await listo();
    fireEvent.click(within(resultado("SIMIT del propietario")).getByRole("radio", { name: "Saneado" }));
    await waitFor(() => expect(estadoFila("SIMIT del propietario")).toHaveTextContent("Guardando…"), { timeout: 2000 });
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}`)).toHaveLength(1);
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_SOAT}`)).toHaveLength(0);
    expect(cuerpoDe(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}`)[0])).toEqual({ resultado: "SANEADO", nota: null, version: 45 });
    await act(async () => soltar(json(renglon({ resultado: "SANEADO", version: 46 }))));
    await waitFor(() => expect(estadoFila("SIMIT del propietario")).toHaveTextContent("Guardado"));
  });

  it("la nota guarda al salir del campo, con tope de 500 caracteres", async () => {
    const f = api(saneamientoBo(), { [`PUT ${RUTA_DETALLE}/renglones/${RID_SOAT}`]: () => json(renglonSoat({ nota: "Pagado en el portal", version: 47 })) });
    montar();
    await listo();
    const nota = screen.getByRole("textbox", { name: "Nota de SOAT" });
    expect(nota).toHaveAttribute("maxLength", "500");
    fireEvent.change(nota, { target: { value: "Pagado en el portal" } });
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_SOAT}`)).toHaveLength(0);
    fireEvent.blur(nota);
    await waitFor(() => expect(estadoFila("SOAT")).toHaveTextContent("Guardado"));
    expect(cuerpoDe(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_SOAT}`)[0])).toEqual({ resultado: "PENDIENTE", nota: "Pagado en el portal", version: 46 });
  });

  it("si el guardado falla, el indicador lo dice, el valor no se pierde y «Reintentar» lo vuelve a enviar", async () => {
    let n = 0;
    const f = api(saneamientoBo(), {
      [`PUT ${RUTA_DETALLE}/renglones/${RID_SOAT}`]: () => (++n === 1 ? json({ error: "x" }, 500) : json(renglonSoat({ nota: "Pagado", version: 47 }))),
    });
    montar();
    await listo();
    const nota = screen.getByRole("textbox", { name: "Nota de SOAT" });
    fireEvent.change(nota, { target: { value: "Pagado" } });
    fireEvent.blur(nota);
    await waitFor(() => expect(estadoFila("SOAT")).toHaveTextContent("No se guardó"));
    expect(screen.getByRole("row", { name: "SOAT" })).toHaveTextContent("Revisa tu conexión e intenta de nuevo.");
    expect(nota).toHaveValue("Pagado");
    fireEvent.click(screen.getByRole("button", { name: "Reintentar SOAT" }));
    await waitFor(() => expect(estadoFila("SOAT")).toHaveTextContent("Guardado"));
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_SOAT}`)).toHaveLength(2);
  });

  it("409 NO_EDITABLE: avisa que ya no está en gestión y recarga el detalle", async () => {
    let n = 0;
    const f = api(() => json(++n === 1 ? saneamientoBo() : saneamientoBo({ estado: "SANEADO", cerrado_en: "2026-11-05T15:00:00+00:00" })), {
      [`PUT ${RUTA_DETALLE}/renglones/${RID_SOAT}`]: () => error("NO_EDITABLE", 409),
    });
    montar();
    await listo();
    const nota = screen.getByRole("textbox", { name: "Nota de SOAT" });
    fireEvent.change(nota, { target: { value: "Pagado" } });
    fireEvent.blur(nota);
    await waitFor(() => expect(llamadas(f, "GET", RUTA_DETALLE)).toHaveLength(2));
    expect(await within(gestion()).findByRole("button", { name: "Reabrir" })).toBeInTheDocument();
  });
});

describe("AC32 — evidencia en PDF", () => {
  const subir = async (archivo: File) => {
    const modal = await dialogo("Evidencia de SIMIT del propietario");
    fireEvent.change(within(modal).getByLabelText("PDF de evidencia de pago"), { target: { files: [archivo] } });
    return modal;
  };

  it("sube un PDF con «Cargar PDF»: el renglón muestra nombre y tamaño con Ver, Reemplazar y Quitar; el foco va a «Ver»", async () => {
    const f = api(saneamientoBo(), {
      [`PUT ${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`]: () =>
        json(renglon({ evidencia: { nombre: "pago.pdf", tamano: 2048, subida_en: "2026-11-05T15:00:00+00:00" }, version: 46 })),
    });
    montar();
    await listo();
    fireEvent.click(screen.getByRole("button", { name: "Cargar evidencia de SIMIT del propietario" }));
    const modal = await subir(pdf());
    const boton = within(modal).getByRole("button", { name: "Subir" });
    await waitFor(() => expect(boton).not.toBeDisabled());
    fireEvent.click(boton);
    const ver = await screen.findByRole("button", { name: "Ver evidencia de SIMIT del propietario" });
    expect(screen.queryByRole("dialog")).toBeNull();
    const fila = screen.getByRole("row", { name: "SIMIT del propietario" });
    expect(fila).toHaveTextContent("pago.pdf");
    expect(fila).toHaveTextContent("2 KB");
    expect(within(fila).getByRole("button", { name: "Reemplazar evidencia de SIMIT del propietario" })).toBeInTheDocument();
    expect(within(fila).getByRole("button", { name: "Quitar evidencia de SIMIT del propietario" })).toBeInTheDocument();
    await waitFor(() => expect(ver).toHaveFocus());
    expect(region()).toHaveTextContent("Evidencia cargada para SIMIT del propietario.");
    const [, init] = llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`)[0] as [string, RequestInit];
    expect((init.body as FormData).get("archivo")).toBeInstanceOf(File);
  });

  it("rechaza antes de subir: otra extensión, un .pdf sin firma %PDF-, más de 10 MB y dos archivos a la vez", async () => {
    const f = api(saneamientoBo());
    montar();
    await listo();
    fireEvent.click(screen.getByRole("button", { name: "Cargar evidencia de SIMIT del propietario" }));
    let modal = await subir(new File(["hola"], "nota.txt", { type: "text/plain" }));
    expect(within(modal).getByRole("alert")).toHaveTextContent("Formato no admitido");
    modal = await subir(new File(["<html>%PDF-1.7"], "falso.pdf", { type: "application/pdf" }));
    await waitFor(() => expect(within(modal).getByRole("alert")).toHaveTextContent("El archivo no es un PDF válido."));
    expect(within(modal).getByRole("button", { name: "Subir" })).toBeDisabled();
    const grande = pdf("grande.pdf");
    Object.defineProperty(grande, "size", { value: 10 * 1024 * 1024 + 1 });
    modal = await subir(grande);
    expect(within(modal).getByRole("alert")).toHaveTextContent("El archivo supera el tamaño máximo de 10 MB.");
    fireEvent.drop(within(modal).getByTestId("zona-arrastre"), { dataTransfer: { files: [pdf("a.pdf"), pdf("b.pdf")] } });
    await waitFor(() => expect(within(modal).getByText("Elige un solo archivo PDF.")).toBeInTheDocument());
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`)).toHaveLength(0);
  });

  it.each([
    [422, "ARCHIVO_NO_ES_PDF", "El archivo no es un PDF válido."],
    [413, "ARCHIVO_MUY_GRANDE", "El archivo supera 10 MB."],
    [400, "ARCHIVO_REQUERIDO", "Elige un archivo PDF."],
    [409, "DEMASIADOS_REEMPLAZOS", "Este concepto ya llegó al máximo de reemplazos de la evidencia."],
  ])("%s %s del servidor: muestra «%s» y el renglón conserva su evidencia anterior", async (status, code, texto) => {
    const previa = { nombre: "anterior.pdf", tamano: 1024, subida_en: "2026-11-04T15:00:00+00:00" };
    api(saneamientoBo({ renglones: [renglon({ evidencia: previa }), renglonSoat()] }), {
      [`PUT ${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`]: () => error(code, status),
    });
    montar();
    await listo();
    fireEvent.click(screen.getByRole("button", { name: "Reemplazar evidencia de SIMIT del propietario" }));
    const modal = await subir(pdf());
    const boton = within(modal).getByRole("button", { name: "Subir" });
    await waitFor(() => expect(boton).not.toBeDisabled());
    fireEvent.click(boton);
    await waitFor(() => expect(within(modal).getByRole("alert")).toHaveTextContent(texto));
    expect(screen.getByRole("row", { name: "SIMIT del propietario" })).toHaveTextContent("anterior.pdf");
  });

  it("«Quitar» la evidencia pide confirmación (foco en Cancelar) y la quita", async () => {
    const previa = { nombre: "anterior.pdf", tamano: 1024, subida_en: "2026-11-04T15:00:00+00:00" };
    // El servidor simulado refleja el DELETE: la gestión relee el detalle para tener las versiones al día.
    let actual = saneamientoBo({ renglones: [renglon({ evidencia: previa }), renglonSoat()] });
    const f = api(() => json(actual), {
      [`DELETE ${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`]: () => {
        actual = saneamientoBo({ renglones: [renglon({ version: 47 }), renglonSoat()] });
        return new Response(null, { status: 204 });
      },
    });
    montar();
    await listo();
    fireEvent.click(screen.getByRole("button", { name: "Quitar evidencia de SIMIT del propietario" }));
    const modal = await dialogo("¿Quitar la evidencia?");
    await waitFor(() => expect(within(modal).getByRole("button", { name: "Cancelar" })).toHaveFocus());
    fireEvent.click(within(modal).getByRole("button", { name: "Quitar" }));
    expect(await screen.findByRole("button", { name: "Cargar evidencia de SIMIT del propietario" })).toBeInTheDocument();
    expect(llamadas(f, "DELETE", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`)).toHaveLength(1);
  });

  it("«Ver» descarga la evidencia por el backoffice con ?empresa=", async () => {
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
    Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
    const previa = { nombre: "anterior.pdf", tamano: 1024, subida_en: "2026-11-04T15:00:00+00:00" };
    const f = api(saneamientoBo({ renglones: [renglon({ evidencia: previa })] }), {
      [`GET ${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`]: () => new Response("%PDF-1.7", { headers: { "Content-Type": "application/pdf" } }),
    });
    montar();
    await listo();
    fireEvent.click(screen.getByRole("button", { name: "Ver evidencia de SIMIT del propietario" }));
    await waitFor(() => expect(llamadas(f, "GET", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}/evidencia`)).toHaveLength(1));
  });
});

describe("AC33 — otro concepto, quitar renglón y observación", () => {
  it("«Agregar otro concepto» valida de 3 a 200 caracteres, agrega el renglón «Otro» y lleva el foco a su resultado", async () => {
    const nuevo = renglon({ id: "00000000-0000-7000-8000-00000000r009", fuente: "OTRO", hallazgo: null, descripcion: "Embargo judicial", automatico: false, version: 1 });
    let actual = saneamientoBo();
    const f = api(() => json(actual), {
      [`POST ${RUTA_DETALLE}/renglones`]: () => {
        actual = saneamientoBo({ renglones: [renglon(), renglonSoat(), nuevo], version: 124 });
        return json(nuevo, 201);
      },
    });
    montar();
    await listo();
    fireEvent.click(within(gestion()).getByRole("button", { name: "Agregar otro concepto" }));
    const modal = await dialogo("Agregar otro concepto");
    const campo = within(modal).getByLabelText("Descripción");
    expect(campo).toHaveAttribute("maxLength", "200");
    fireEvent.change(campo, { target: { value: "ab" } });
    fireEvent.click(within(modal).getByRole("button", { name: "Agregar" }));
    expect(within(modal).getByText("Escribe al menos 3 caracteres.")).toBeInTheDocument();
    expect(llamadas(f, "POST", `${RUTA_DETALLE}/renglones`)).toHaveLength(0);
    fireEvent.change(campo, { target: { value: "Embargo judicial" } });
    fireEvent.click(within(modal).getByRole("button", { name: "Agregar" }));
    const fila = await screen.findByRole("row", { name: "Otro: Embargo judicial" });
    expect(fila).toHaveTextContent("Agregado por FLIT");
    expect(cuerpoDe(llamadas(f, "POST", `${RUTA_DETALLE}/renglones`)[0])).toEqual({ descripcion: "Embargo judicial" });
    await waitFor(() => expect(within(resultado("Otro: Embargo judicial")).getByRole("radio", { name: "Pendiente" })).toHaveFocus());
    expect(region()).toHaveTextContent("Se agregó el concepto Embargo judicial.");
  });

  it("«Quitar concepto» con confirmación: el renglón desaparece y el foco pasa al siguiente", async () => {
    let actual = saneamientoBo();
    const f = api(() => json(actual), {
      [`DELETE ${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}`]: () => {
        actual = saneamientoBo({ renglones: [renglonSoat()], version: 124 });
        return new Response(null, { status: 204 });
      },
    });
    montar();
    await listo();
    fireEvent.click(screen.getByRole("button", { name: "Quitar concepto SIMIT del propietario" }));
    const modal = await dialogo("¿Quitar el concepto?");
    expect(modal).toHaveTextContent("SIMIT del propietario. Quedará en el historial.");
    fireEvent.click(within(modal).getByRole("button", { name: "Quitar" }));
    await waitFor(() => expect(screen.queryByRole("row", { name: "SIMIT del propietario" })).toBeNull());
    expect(llamadas(f, "DELETE", `${RUTA_DETALLE}/renglones/${RID_PROPIETARIO}`)).toHaveLength(1);
    await waitFor(() => expect(within(resultado("SOAT")).getByRole("radio", { name: "Pendiente" })).toHaveFocus());
    expect(region()).toHaveTextContent("Se quitó el concepto SIMIT del propietario.");
  });

  it("la observación general (hasta 1000) se guarda al salir del campo, con indicador y anuncio", async () => {
    const f = api(saneamientoBo(), { [`PUT ${RUTA_DETALLE}/observacion`]: () => json(saneamientoBo({ observacion_flit: "Se pagó todo.", version: 124 })) });
    montar();
    await listo();
    const campo = screen.getByLabelText("Observación para la compañía");
    expect(campo).toHaveAttribute("maxLength", "1000");
    fireEvent.change(campo, { target: { value: "Se pagó todo." } });
    fireEvent.blur(campo);
    await waitFor(() => expect(screen.getByTestId("indicador-observacion")).toHaveTextContent("Guardado"));
    expect(cuerpoDe(llamadas(f, "PUT", `${RUTA_DETALLE}/observacion`)[0])).toEqual({ observacion: "Se pagó todo.", version: 123 });
    expect(region()).toHaveTextContent("Observación: Guardado");
  });

  it("la observación no se envía al salir si no cambió", async () => {
    const f = api(saneamientoBo({ observacion_flit: "Igual" }));
    montar();
    await listo();
    fireEvent.blur(screen.getByLabelText("Observación para la compañía"));
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/observacion`)).toHaveLength(0);
  });

  it("si la observación falla: alerta bajo el campo con «Reintentar»", async () => {
    let n = 0;
    const f = api(saneamientoBo(), {
      [`PUT ${RUTA_DETALLE}/observacion`]: () => (++n === 1 ? json({ error: "x" }, 500) : json(saneamientoBo({ observacion_flit: "Nota", version: 124 }))),
    });
    montar();
    await listo();
    const campo = screen.getByLabelText("Observación para la compañía");
    fireEvent.change(campo, { target: { value: "Nota" } });
    fireEvent.blur(campo);
    expect(await screen.findByText("Revisa tu conexión e intenta de nuevo.", { selector: "[role=alert]" })).toBeInTheDocument();
    expect(screen.getByTestId("indicador-observacion")).toHaveTextContent("No se guardó");
    fireEvent.click(screen.getByRole("button", { name: "Reintentar la observación" }));
    await waitFor(() => expect(screen.getByTestId("indicador-observacion")).toHaveTextContent("Guardado"));
    expect(llamadas(f, "PUT", `${RUTA_DETALLE}/observacion`)).toHaveLength(2);
  });
});

describe("AC34 — cierre", () => {
  const cerrarSaneado = () => within(gestion()).getByRole("button", { name: "Cerrar como saneado" });

  it.each<[string, RenglonSaneamiento[], string]>([
    ["con un renglón pendiente", [renglon({ resultado: "SANEADO" }), renglonSoat()], "Para cerrar como saneado ningún concepto puede quedar pendiente (faltan 1)."],
    ["sin renglones", [], "Para cerrar como saneado agrega al menos un concepto."],
    ["sin ninguno saneado", [renglon({ resultado: "NO_PROCEDE" })], "Para cerrar como saneado al menos un concepto debe quedar saneado."],
  ])("%s: «Cerrar como saneado» queda aria-disabled y explica lo que falta por aria-describedby", async (_caso, renglones, motivo) => {
    api(saneamientoBo({ renglones }));
    montar();
    await listo();
    expect(cerrarSaneado()).toHaveAttribute("aria-disabled", "true");
    expect(cerrarSaneado()).toHaveAccessibleDescription(motivo);
    expect(screen.getByText(motivo)).toBeVisible();
    fireEvent.click(cerrarSaneado());
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("cumpliendo la regla, confirma y queda SANEADO", async () => {
    const f = api(saneamientoBo({ renglones: [renglon({ resultado: "SANEADO" }), renglonSoat({ resultado: "NO_PROCEDE" })] }), {
      [`POST ${RUTA_DETALLE}/cerrar`]: () => json(saneamientoBo({ estado: "SANEADO", cerrado_en: "2026-11-05T15:00:00+00:00" })),
    });
    montar();
    await listo();
    expect(cerrarSaneado()).not.toHaveAttribute("aria-disabled");
    fireEvent.click(cerrarSaneado());
    const modal = await dialogo("¿Cerrar como saneado?");
    expect(modal).toHaveTextContent("La compañía verá el saneamiento como saneado. El dictamen no cambia.");
    await waitFor(() => expect(within(modal).getByRole("button", { name: "Cancelar" })).toHaveFocus());
    fireEvent.click(within(modal).getByRole("button", { name: "Cerrar como saneado" }));
    expect(await within(gestion()).findByRole("button", { name: "Reabrir" })).toBeInTheDocument();
    expect(cuerpoDe(llamadas(f, "POST", `${RUTA_DETALLE}/cerrar`)[0])).toEqual({ resultado: "SANEADO", version: 123 });
    expect(screen.getByRole("heading", { level: 1, name: "TST001" }).closest("header")).toHaveTextContent("Saneado");
    expect(region()).toHaveTextContent("Saneamiento cerrado como saneado.");
  });

  it("422 CIERRE_INCOMPLETO: el motivo en español en el modal y el estado no cambia", async () => {
    api(saneamientoBo({ renglones: [renglon({ resultado: "SANEADO" })] }), { [`POST ${RUTA_DETALLE}/cerrar`]: () => error("CIERRE_INCOMPLETO", 422) });
    montar();
    await listo();
    fireEvent.click(cerrarSaneado());
    const modal = await dialogo("¿Cerrar como saneado?");
    fireEvent.click(within(modal).getByRole("button", { name: "Cerrar como saneado" }));
    expect(await within(modal).findByRole("alert")).toHaveTextContent(
      "Para cerrar como saneado ningún concepto puede quedar pendiente y al menos uno debe quedar saneado.",
    );
    expect(screen.getByRole("heading", { level: 1, name: "TST001" }).closest("header")).toHaveTextContent("En saneamiento");
  });

  it("«Cerrar como no procede» pide el motivo y queda NO_PROCEDE", async () => {
    const f = api(saneamientoBo(), {
      [`POST ${RUTA_DETALLE}/cerrar`]: () => json(saneamientoBo({ estado: "NO_PROCEDE", motivo_cierre: "El vehículo tiene embargo.", cerrado_en: "2026-11-05T15:00:00+00:00" })),
    });
    montar();
    await listo();
    fireEvent.click(within(gestion()).getByRole("button", { name: "Cerrar como no procede" }));
    const modal = await dialogo("Cerrar como no procede");
    fireEvent.change(within(modal).getByLabelText("Motivo"), { target: { value: "El vehículo tiene embargo." } });
    fireEvent.click(within(modal).getByRole("button", { name: "Cerrar como no procede" }));
    expect(await within(gestion()).findByRole("button", { name: "Reabrir" })).toBeInTheDocument();
    expect(cuerpoDe(llamadas(f, "POST", `${RUTA_DETALLE}/cerrar`)[0])).toEqual({ resultado: "NO_PROCEDE", motivo: "El vehículo tiene embargo.", version: 123 });
    expect(region()).toHaveTextContent("Saneamiento cerrado: no procede.");
  });

  it("409 TRANSICION_INVALIDA: aviso en el modal con «Recargar»", async () => {
    const f = api(saneamientoBo(), { [`POST ${RUTA_DETALLE}/cerrar`]: () => error("TRANSICION_INVALIDA", 409) });
    montar();
    await listo();
    fireEvent.click(within(gestion()).getByRole("button", { name: "Cerrar como no procede" }));
    const modal = await dialogo("Cerrar como no procede");
    fireEvent.change(within(modal).getByLabelText("Motivo"), { target: { value: "El vehículo tiene embargo." } });
    fireEvent.click(within(modal).getByRole("button", { name: "Cerrar como no procede" }));
    expect(await within(modal).findByRole("alert")).toHaveTextContent("El estado cambió mientras tanto. Recarga para ver el estado actual.");
    fireEvent.click(within(modal).getByRole("button", { name: "Recargar" }));
    await waitFor(() => expect(llamadas(f, "GET", RUTA_DETALLE)).toHaveLength(2));
  });
});

describe("AC35 — solo lectura y reapertura", () => {
  const cerrado = () =>
    saneamientoBo({
      estado: "SANEADO",
      cerrado_en: "2026-11-05T15:00:00+00:00",
      observacion_flit: "Se pagó todo.",
      renglones: [renglon({ resultado: "SANEADO", nota: "Pagado", evidencia: { nombre: "pago.pdf", tamano: 2048, subida_en: "2026-11-05T15:00:00+00:00" } })],
    });

  it("SANEADO: renglones, evidencia y observación de solo lectura; solo ofrece «Reabrir»", async () => {
    api(cerrado());
    montar();
    await listo();
    expect(screen.queryByRole("radiogroup")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    expect(gestion()).toHaveTextContent("Se pagó todo.");
    expect(gestion()).toHaveTextContent("Pagado");
    const botones = within(gestion())
      .getAllByRole("button")
      .map((b) => b.textContent?.trim());
    expect(botones).toEqual(["Ver evidencia", "Reabrir"]);
  });

  it("«Reabrir» pide el motivo y vuelve a EN_GESTION con los renglones como estaban", async () => {
    const f = api(cerrado(), {
      [`POST ${RUTA_DETALLE}/reabrir`]: () => json({ ...cerrado(), estado: "EN_GESTION", cerrado_en: null, version: 124 }),
    });
    montar();
    await listo();
    fireEvent.click(within(gestion()).getByRole("button", { name: "Reabrir" }));
    const modal = await dialogo("Reabrir el saneamiento");
    const motivo = within(modal).getByLabelText("Motivo");
    fireEvent.change(motivo, { target: { value: "x".repeat(9) } });
    fireEvent.click(within(modal).getByRole("button", { name: "Reabrir" }));
    expect(within(modal).getByText("Escribe al menos 10 caracteres.")).toBeInTheDocument();
    fireEvent.change(motivo, { target: { value: "Falta la evidencia del SOAT." } });
    fireEvent.click(within(modal).getByRole("button", { name: "Reabrir" }));
    expect(await screen.findByRole("radiogroup", { name: "Resultado de SIMIT del propietario" })).toBeInTheDocument();
    expect(within(resultado("SIMIT del propietario")).getByRole("radio", { name: "Saneado" })).toHaveAttribute("aria-checked", "true");
    expect(cuerpoDe(llamadas(f, "POST", `${RUTA_DETALLE}/reabrir`)[0])).toEqual({ motivo: "Falta la evidencia del SOAT.", version: 123 });
    expect(region()).toHaveTextContent("Saneamiento reabierto.");
  });
});

describe("AC36 — historial completo", () => {
  it("todos los eventos, el más reciente primero, con fecha, estado anterior y nuevo, fuente, motivo e id de usuario", async () => {
    api(
      saneamientoBo({
        historial: [
          { evento: "SOLICITADO", ocurrido_en: "2026-11-03T15:00:00+00:00", actor: "COMPANIA", fuente: null, motivo: null, usuario_id: "00000000-0000-7000-8000-000000000001" },
          { evento: "TOMADO", ocurrido_en: "2026-11-04T15:00:00+00:00", actor: "FLIT", fuente: null, motivo: null, usuario_id: USUARIO_FLIT },
          { evento: "RENGLON_ACTUALIZADO", ocurrido_en: "2026-11-04T16:00:00+00:00", actor: "FLIT", fuente: "SOAT", motivo: null, usuario_id: USUARIO_FLIT },
          { evento: "OBSERVACION_EDITADA", ocurrido_en: "2026-11-04T17:00:00+00:00", actor: "FLIT", fuente: null, motivo: null, usuario_id: USUARIO_FLIT },
          { evento: "CERRADO_NO_PROCEDE", ocurrido_en: "2026-11-05T15:00:00+00:00", actor: "FLIT", fuente: null, motivo: "El vehículo tiene embargo.", usuario_id: USUARIO_FLIT },
          { evento: "REABIERTO", ocurrido_en: "2026-11-06T15:00:00+00:00", actor: "FLIT", fuente: null, motivo: "Llegó el levantamiento.", usuario_id: USUARIO_FLIT },
        ],
      }),
    );
    montar();
    await listo();
    const lista = screen.getByRole("list", { name: "Historial" });
    const items = within(lista).getAllByRole("listitem");
    expect(items).toHaveLength(6);
    expect(items[0]).toHaveTextContent("06/11/2026");
    expect(items[0]).toHaveTextContent("FLIT reabrió el saneamiento");
    expect(items[0]).toHaveTextContent("Saneamiento no procede → En saneamiento");
    expect(items[0]).toHaveTextContent("Motivo: Llegó el levantamiento.");
    expect(items[0]).toHaveTextContent("FLIT · usuario a1b2c3d4");
    expect(items[1]).toHaveTextContent("En saneamiento → Saneamiento no procede");
    expect(items[2]).toHaveTextContent("Se editó la observación");
    expect(items[3]).toHaveTextContent("Se actualizó el concepto SOAT");
    expect(items[4]).toHaveTextContent("Saneamiento solicitado → En saneamiento");
    expect(items[5]).toHaveTextContent("Se solicitó el saneamiento");
    expect(items[5]).toHaveTextContent("Compañía");
  });
});

describe("AC37 — conflicto de versión (negativo)", () => {
  it("409 VERSION_DESACTUALIZADA al guardar un renglón: error en la fila y NotificacionPersistente; «Recargar» trae los datos vigentes", async () => {
    let n = 0;
    const f = api(() => json(++n === 1 ? saneamientoBo() : saneamientoBo({ renglones: [renglon(), renglonSoat({ nota: "Lo cambió otra persona", version: 50 })] })), {
      [`PUT ${RUTA_DETALLE}/renglones/${RID_SOAT}`]: () => error("VERSION_DESACTUALIZADA", 409),
    });
    montar();
    await listo();
    const nota = screen.getByRole("textbox", { name: "Nota de SOAT" });
    fireEvent.change(nota, { target: { value: "Mi versión" } });
    fireEvent.blur(nota);
    const aviso = await screen.findByText("Otra persona cambió este saneamiento");
    expect(screen.getByRole("row", { name: "SOAT" })).toHaveTextContent("Otra persona cambió este concepto.");
    const notificacion = aviso.closest("[role=status]") as HTMLElement;
    fireEvent.click(within(notificacion).getByRole("button", { name: "Recargar" }));
    await waitFor(() => expect(llamadas(f, "GET", RUTA_DETALLE)).toHaveLength(2));
    await waitFor(() => expect(screen.getByRole("textbox", { name: "Nota de SOAT" })).toHaveValue("Lo cambió otra persona"));
    expect(screen.queryByText("Otra persona cambió este saneamiento")).toBeNull();
  });

  it("409 VERSION_DESACTUALIZADA al anular: cierra el modal y abre la NotificacionPersistente", async () => {
    api(saneamientoBo(), { [`POST ${RUTA_DETALLE}/anular`]: () => error("VERSION_DESACTUALIZADA", 409) });
    montar();
    await listo();
    fireEvent.click(within(gestion()).getByRole("button", { name: "Anular" }));
    const modal = await dialogo("Anular el saneamiento");
    fireEvent.change(within(modal).getByLabelText("Motivo"), { target: { value: "La compañía lo pidió por error." } });
    fireEvent.click(within(modal).getByRole("button", { name: "Anular" }));
    expect(await screen.findByText("Otra persona cambió este saneamiento")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("409 VERSION_DESACTUALIZADA al guardar la observación: NotificacionPersistente", async () => {
    api(saneamientoBo(), { [`PUT ${RUTA_DETALLE}/observacion`]: () => error("VERSION_DESACTUALIZADA", 409) });
    montar();
    await listo();
    const campo = screen.getByLabelText("Observación para la compañía");
    fireEvent.change(campo, { target: { value: "Nota" } });
    fireEvent.blur(campo);
    expect(await screen.findByText("Otra persona cambió este saneamiento")).toBeInTheDocument();
  });
});
