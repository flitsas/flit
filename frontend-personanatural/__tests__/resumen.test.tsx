import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { claveSolicitud } from "@/components/tramite/useSolicitud";
import { claveCotizacion } from "@/lib/tramite/cotizacion";
import { abrirResumen, adjunto, DOC_COMPRADOR, DOC_VENDEDOR, resumenVisible, solicitudCompleta } from "./fixtures-envio";
import { nodo } from "./montar-wizard";
import { json } from "./utilidades";

const seccion = (nombre: string) => within(resumenVisible()).getByRole("region", { name: nombre });

describe("Paso 4 · Resumen (F5 reducida)", () => {
  it("reabre el borrador completo directamente en el paso Resumen", async () => {
    await abrirResumen();

    expect(nodo(4)).toHaveAttribute("aria-current", "step");
    expect(screen.getByRole("navigation", { name: "Ruta de navegación" })).toHaveTextContent("Dashboard > Trámites > Trámite A > Resumen");
  });

  it("muestra solo lectura lo que el ciudadano llenó: trámite, vehículo, campos, condiciones comerciales y documentos", async () => {
    await abrirResumen();

    const tramite = seccion("Trámite");
    expect(tramite).toHaveTextContent("Tipo de trámiteTrámite A");
    expect(tramite).toHaveTextContent("Organismo de TránsitoCiudad Uno");
    expect(tramite).toHaveTextContent("Radicado del portalPN-2026-0042");
    expect(seccion("Vehículo")).toHaveTextContent("PlacaABC123");
    expect(seccion("Datos del vehículo")).toHaveTextContent("Cilindraje1600");
    expect(seccion("Datos del vehículo")).toHaveTextContent("ColorAZUL");

    const comercial = seccion("Condiciones comerciales");
    expect(comercial).toHaveTextContent("Valor comercial$ 45.000.000 COP");
    expect(comercial).toHaveTextContent("PrendaSí");
    expect(comercial).toHaveTextContent("AcreedorBANCO DE PRUEBA");
    expect(comercial).toHaveTextContent("NIT del acreedor900123456");
    expect(seccion("Información adicional")).toHaveTextContent("TransformacionesCambio de color");
    expect(seccion("Información adicional")).toHaveTextContent("ObservacionesEntrega la semana próxima");

    // Solo el nombre del documento, el del archivo y su peso: nunca el contenido. El comprobante de pago no se muestra.
    const documentos = seccion("Documentos cargados");
    expect(documentos).toHaveTextContent("Paz y salvo de impuestospaz-y-salvo.pdf · 2.5 MB");
    expect(documentos).not.toHaveTextContent("soporte.pdf");
  });

  it("cada actor sale con el nombre de su rol y el documento, el correo y el celular enmascarados", async () => {
    await abrirResumen();

    const actores = seccion("Actores");
    expect(actores).toHaveTextContent("Propietario (vendedor)");
    expect(actores).toHaveTextContent("DocumentoCC ••••6789");
    expect(actores).toHaveTextContent("NombreANA RUIZ");
    expect(actores).toHaveTextContent("Correoan•••@correo.co");
    expect(actores).toHaveTextContent("Celular••••••••67");
    expect(actores).toHaveTextContent("DirecciónSin registrar");
    expect(actores).toHaveTextContent("Comprador");
    expect(actores).toHaveTextContent("DocumentoCC ••••7654");
    expect(actores).toHaveTextContent("DirecciónRegistrada");

    const texto = resumenVisible().textContent ?? "";
    for (const crudo of [DOC_VENDEDOR, DOC_COMPRADOR, "ana.ruiz@correo.co", "luis.perez@mail.co", "3001234567", "3109876543", "Calle 1 # 2-3"]) expect(texto).not.toContain(crudo);
  });

  it("«Editar» lleva al paso donde se llenó cada sección", async () => {
    // Actores guarda al continuar: el back responde con el borrador y su versión nueva.
    await abrirResumen({ "PATCH /solicitudes/s-1/actores": () => json(solicitudCompleta({ version: 8 })) });

    fireEvent.click(within(seccion("Actores")).getByRole("button", { name: "Editar Actores" }));
    expect(nodo(2)).toHaveAttribute("aria-current", "step");

    fireEvent.click(nodo(4));
    await waitFor(() => expect(nodo(4)).toHaveAttribute("aria-current", "step"));
    fireEvent.click(within(seccion("Condiciones comerciales")).getByRole("button", { name: "Editar Condiciones comerciales" }));
    expect(nodo(3)).toHaveAttribute("aria-current", "step");

    fireEvent.click(nodo(4));
    await waitFor(() => expect(nodo(4)).toHaveAttribute("aria-current", "step"));
    fireEvent.click(within(seccion("Vehículo")).getByRole("button", { name: "Editar Vehículo" }));
    expect(nodo(1)).toHaveAttribute("aria-current", "step");
  });

  it("la sección del trámite (tipo, organismo y radicado) no se edita", async () => {
    await abrirResumen();

    expect(within(seccion("Trámite")).queryByRole("button")).toBeNull();
  });

  it("sin confirmar no deja continuar y dice por qué; al confirmar habilita «Continuar» y pasa al envío", async () => {
    await abrirResumen();

    const continuar = screen.getByRole("button", { name: "Continuar" });
    expect(continuar).toBeDisabled();
    expect(continuar).toHaveAccessibleDescription("Confirma que revisaste la información para continuar.");
    const casilla = screen.getByLabelText("Revisé la información y autorizo el envío");
    expect(casilla).not.toBeChecked();

    fireEvent.click(casilla);
    expect(continuar).toBeEnabled();
    fireEvent.click(continuar);

    await waitFor(() => expect(nodo(5)).toHaveAttribute("aria-current", "step"));
  });

  it("si el borrador cambia después de confirmar (cambia su versión), la confirmación se pide de nuevo", async () => {
    const { client } = await abrirResumen();
    fireEvent.click(screen.getByLabelText("Revisé la información y autorizo el envío"));
    expect(screen.getByRole("button", { name: "Continuar" })).toBeEnabled();

    act(() => client.setQueryData(claveSolicitud("s-1"), solicitudCompleta({ version: 8 })));

    await waitFor(() => expect(screen.getByLabelText("Revisé la información y autorizo el envío")).not.toBeChecked());
    expect(screen.getByRole("button", { name: "Continuar" })).toBeDisabled();
  });

  it("muestra el valor del trámite solo si ya se calculó en la barra lateral, y sin cobro", async () => {
    const { client } = await abrirResumen();
    expect(within(seccion("Trámite")).queryByText(/informativo/)).toBeNull();

    act(() => client.setQueryData(claveCotizacion("o-1", "TIPO_A", ["CAMBIO_COLOR"]), { lineas: [], totalCop: 285400, versionTarifa: "v1", adicionalesNoAplicados: [] }));

    await waitFor(() => expect(seccion("Trámite")).toHaveTextContent("Valor del trámite (informativo)$ 285.400 COP"));
  });

  it("lo que solo le toca al envío (el requisito de cobro que aún pide el back) no se lista como pendiente ni bloquea el resumen", async () => {
    await abrirResumen({}, solicitudCompleta({ pendientes: [{ codigo: "comprobante_pago_requerido", referencia: null, paso: null }] }));

    expect(within(resumenVisible()).queryByText(/Aún falta información/)).toBeNull();
    fireEvent.click(screen.getByLabelText("Revisé la información y autorizo el envío"));
    expect(screen.getByRole("button", { name: "Continuar" })).toBeEnabled();
  });

  it("sin documentos, comercial ni transformaciones no deja secciones vacías", async () => {
    await abrirResumen({}, solicitudCompleta({ adjuntos: [adjunto("a2", "comprobante_pago", "soporte.pdf")], datos: { diagnostico: { puedeContinuar: true, requisitos: [], fecha: "2026-10-28T20:23:00Z" } } }));

    for (const nombre of ["Documentos cargados", "Condiciones comerciales", "Información adicional"]) expect(within(resumenVisible()).queryByRole("region", { name: nombre })).toBeNull();
  });

  it("no hay rastro de pago: ni facturación, ni datos bancarios, ni comprobante, ni PSE", async () => {
    await abrirResumen();

    const pantalla = document.body.textContent ?? "";
    expect(pantalla).not.toMatch(/pago|PSE\b|transferencia|bancari|bancolombia|factura|comprobante|convenio/i);
    expect(screen.getByRole("list", { name: "Pasos del trámite" })).toHaveTextContent(/Diagnóstico.*Actores.*Requisitos.*Resumen.*Envío/);
  });

  it("Habeas Data: nada del ciudadano en el almacenamiento, en la URL ni en la consola", async () => {
    const espias = (["log", "info", "warn", "error", "debug"] as const).map((n) => vi.spyOn(console, n).mockImplementation(() => undefined));
    await abrirResumen();
    fireEvent.click(screen.getByLabelText("Revisé la información y autorizo el envío"));

    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(window.location.href).not.toMatch(new RegExp(`${DOC_VENDEDOR}|${DOC_COMPRADOR}|ABC123`));
    for (const e of espias) expect(e).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });
});
