// Épica #14021 — Consola de Persona Natural: el estado de la configuración dice qué falta para que el portal ofrezca un
// trámite, cada paso lleva a su pestaña y el resumen «lo que ve hoy el ciudadano» sale de los mismos datos que las
// tablas. La API es un doble; las reglas de cálculo se usan reales.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { PersonaNaturalConsole } from "../PersonaNaturalConsole";
import { calcularResumen } from "../PnEstadoConfiguracion";
import {
  establecerTipoPn,
  fetchOrganismosPn,
  fetchTiposPn,
  type OrganismoPn,
  type TipoPn,
} from "@/lib/api/admin-pn-config";
import { fetchTarifasPn } from "@/lib/api/admin-pn-tarifas";

vi.mock("@/lib/api/admin-pn-config", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-config")>();
  return {
    ...real,
    fetchTiposPn: vi.fn(),
    fetchOrganismosPn: vi.fn(),
    establecerTipoPn: vi.fn(),
    establecerOrganismoPn: vi.fn(),
    fetchParametrosPn: vi.fn().mockResolvedValue({}),
  };
});
vi.mock("@/lib/api/admin-pn-tarifas", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-tarifas")>();
  return { ...real, fetchTarifasPn: vi.fn(), fetchHistorialTarifaPn: vi.fn(), registrarTarifasPn: vi.fn() };
});

const tipo = (over: Partial<TipoPn>): TipoPn => ({
  id: "t1",
  codigo: "TRASPASO",
  nombre: "Traspaso",
  familia: "traspaso",
  elegible: true,
  motivo: "",
  habilitado: true,
  ofrecible: true,
  organismosConTarifaVigente: 2,
  ...over,
});
const organismo = (over: Partial<OrganismoPn>): OrganismoPn => ({
  id: "o1",
  codigo: "BOG",
  nombre: "Movilidad de Bogotá",
  municipio: "Bogotá",
  departamento: "Bogotá D.C.",
  habilitado: true,
  tiposConTarifaVigente: 1,
  ...over,
});

function renderConsola() {
  return render(
    <ToastProvider>
      <PersonaNaturalConsole />
    </ToastProvider>,
  );
}

describe("calcularResumen", () => {
  it("no cuenta como hechos los trámites no elegibles ni los organismos que ya no están activos en FLIT", () => {
    const r = calcularResumen(
      [tipo({ elegible: false, ofrecible: true }), tipo({ id: "t2", activoEnFlit: false }), tipo({ id: "t3" })],
      [organismo({ activoEnFlit: false }), organismo({ id: "o2" })],
    );
    expect(r.tiposActivos).toBe(1);
    expect(r.organismosHabilitados).toBe(1);
    expect(r.organismosConTarifa).toBe(1);
  });

  it("cuenta solo lo que el ciudadano puede ver: trámites activados con tarifa y organismos habilitados con tarifa", () => {
    const r = calcularResumen(
      [tipo({}), tipo({ id: "t2", organismosConTarifaVigente: 0 }), tipo({ id: "t3", habilitado: false, ofrecible: false })],
      [organismo({}), organismo({ id: "o2", tiposConTarifaVigente: 0 }), organismo({ id: "o3", habilitado: false, tiposConTarifaVigente: 0 })],
    );
    expect(r).toEqual({
      tiposActivos: 2,
      tiposConTarifa: 1,
      organismosHabilitados: 2,
      organismosSinTarifa: 1,
      organismosConTarifa: 1,
    });
  });
});

describe("PersonaNaturalConsole (#14021)", () => {
  it("con los tres pasos hechos el estado es compacto, se expande con «Ver pasos» y se vuelve a ocultar", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({})]);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([organismo({})]);
    renderConsola();
    expect(await screen.findByText(/configuración completa\. hoy el ciudadano puede iniciar 1 tipo de trámite en 1 organismo/i)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: /pasos de la configuración/i })).not.toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /ver pasos/i }));
    expect(screen.getByRole("list", { name: /pasos de la configuración/i })).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /ocultar pasos/i }));
    expect(screen.queryByRole("list", { name: /pasos de la configuración/i })).not.toBeInTheDocument();
  });

  it("los botones del estado abren la pestaña correcta y llevan el foco a su título", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({})]);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([organismo({})]);
    renderConsola();
    await screen.findByText(/configuración completa/i);
    await ue.click(screen.getByRole("button", { name: /ver pasos/i }));
    await ue.click(screen.getByRole("button", { name: /elegir organismos/i }));
    expect(screen.getByRole("tab", { name: "Organismos", selected: true })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("heading", { name: /organismos de tránsito/i })).toHaveFocus());
    await ue.click(screen.getByRole("button", { name: /elegir trámites/i }));
    expect(screen.getByRole("tab", { name: "Trámites", selected: true })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("heading", { name: /trámites que ofrece el portal/i })).toHaveFocus());
  });

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({})]);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([organismo({})]);
    vi.mocked(fetchTarifasPn).mockResolvedValue([]);
    vi.mocked(establecerTipoPn).mockResolvedValue(undefined);
  });

  it("con todo configurado dice los tres pasos hechos y cuántos trámites y organismos ve el ciudadano", async () => {
    renderConsola();
    await screen.findByText(/configuración completa/i);
    await userEvent.setup().click(screen.getByRole("button", { name: /ver pasos/i }));
    expect(await screen.findByRole("heading", { name: /estado de la configuración/i })).toBeInTheDocument();
    expect(await screen.findByText(/hoy el ciudadano puede iniciar 1 tipo de trámite en 1 organismo de tránsito/i)).toBeInTheDocument();
    const lista = screen.getByRole("list", { name: /pasos de la configuración/i });
    const items = within(lista).getAllByRole("listitem");
    expect(items).toHaveLength(3);
    for (const li of items) expect(within(li).getByText("Hecho")).toBeInTheDocument();
    expect(screen.getByText(/necesitas tres cosas/i)).toBeInTheDocument();
  });

  it("sin organismos ni tarifa marca los pasos pendientes y dice qué falta; muestra la advertencia de organismos sin tarifa", async () => {
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({ organismosConTarifaVigente: 0 })]);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([organismo({ habilitado: false, tiposConTarifaVigente: 0 }), organismo({ id: "o2", tiposConTarifaVigente: 0 })]);
    renderConsola();
    expect(await screen.findByText(/todavía no puede iniciar ningún trámite\. falta: una tarifa vigente/i)).toBeInTheDocument();
    const items = within(screen.getByRole("list", { name: /pasos de la configuración/i })).getAllByRole("listitem");
    expect(within(items[0]).getByText("Hecho")).toBeInTheDocument();
    expect(within(items[1]).getByText("Hecho")).toBeInTheDocument();
    expect(within(items[2]).getByText("Pendiente")).toBeInTheDocument();
    expect(screen.getByText(/1 organismo habilitado no tiene tarifa vigente/i)).toBeInTheDocument();
  });

  it("«Configurar tarifas» abre la pestaña Tarifas y lleva el foco a su título", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({ organismosConTarifaVigente: 0 })]);
    renderConsola();
    await screen.findByText(/falta: una tarifa vigente/i);
    expect(screen.getByRole("tab", { name: "Trámites", selected: true })).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /configurar tarifas/i }));
    expect(screen.getByRole("tab", { name: "Tarifas", selected: true })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("heading", { name: /tarifas del portal/i })).toHaveFocus());
  });

  it("al apagar un trámite en la pestaña, el estado de la configuración se vuelve a consultar", async () => {
    const ue = userEvent.setup();
    renderConsola();
    await screen.findByText(/hoy el ciudadano puede iniciar/i);
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({ habilitado: false, ofrecible: false })]);
    await ue.click(await screen.findByRole("switch", { name: /dejar de ofrecer traspaso en el portal/i }));
    await ue.click(within(await screen.findByRole("dialog")).getByRole("button", { name: /^dejar de ofrecer$/i }));
    expect(await screen.findByText(/todavía no puede iniciar ningún trámite/i)).toBeInTheDocument();
  });

  it("si no se puede consultar el estado lo dice y deja reintentar", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTiposPn).mockRejectedValueOnce(new Error("caído")).mockRejectedValueOnce(new Error("caído"));
    renderConsola();
    expect(await screen.findByText(/no se pudo consultar el estado de la configuración/i)).toBeInTheDocument();
    vi.mocked(fetchTiposPn).mockResolvedValue([tipo({})]);
    const reintentos = screen.getAllByRole("button", { name: /reintentar/i });
    await ue.click(reintentos[0]);
    expect(await screen.findByText(/hoy el ciudadano puede iniciar/i)).toBeInTheDocument();
  });

  it("las pestañas usan nombres de negocio y el orden sigue los pasos", async () => {
    renderConsola();
    const nombres = screen.getAllByRole("tab").map((t) => t.textContent);
    expect(nombres).toEqual(["Trámites", "Organismos", "Tarifas", "Ajustes del portal", "Contenido"]);
  });
});
