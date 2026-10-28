// HU #14075 — Pestaña «Organismos y trámites»: ofrecer y dejar de ofrecer (con confirmación) con la tabla estándar (AC1),
// aviso «Falta tarifa» con botón a Tarifas, 422 `tipo_no_elegible`, y los cuatro estados (AC5). La API (HU #14072) es un doble.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import { OrganismosYTiposTab } from "../OrganismosYTiposTab";
import {
  establecerOrganismoPn,
  establecerTipoPn,
  fetchOrganismosPn,
  fetchTiposPn,
  type OrganismoPn,
  type TipoPn,
} from "@/lib/api/admin-pn-config";

vi.mock("@/lib/api/admin-pn-config", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-config")>();
  return { ...real, fetchOrganismosPn: vi.fn(), fetchTiposPn: vi.fn(), establecerOrganismoPn: vi.fn(), establecerTipoPn: vi.fn() };
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
  organismosConTarifaVigente: 3,
  ...over,
});
const organismo = (over: Partial<OrganismoPn>): OrganismoPn => ({
  id: "o1",
  codigo: "BOG",
  nombre: "Secretaría de Movilidad de Bogotá",
  municipio: "Bogotá",
  departamento: "Bogotá D.C.",
  habilitado: true,
  tiposConTarifaVigente: 2,
  ...over,
});

const TIPOS: TipoPn[] = [
  tipo({}),
  tipo({ id: "t2", codigo: "MATRICULA", nombre: "Matrícula inicial", habilitado: true, organismosConTarifaVigente: 0, ofrecible: true }),
  tipo({ id: "t3", codigo: "PRENDA", nombre: "Inscripción de prenda", elegible: false, motivo: "El asistente no está publicado", habilitado: false, ofrecible: false, organismosConTarifaVigente: 0 }),
  tipo({ id: "t4", codigo: "CAMBIO_COLOR", nombre: "Cambio de color", habilitado: false, ofrecible: false, organismosConTarifaVigente: 1 }),
];
const ORGANISMOS: OrganismoPn[] = [
  organismo({}),
  organismo({ id: "o2", codigo: "MED", nombre: "Tránsito de Medellín", municipio: "Medellín", departamento: "Antioquia", habilitado: true, tiposConTarifaVigente: 0 }),
  organismo({ id: "o3", codigo: "CAL", nombre: "Movilidad de Cali", municipio: "Cali", departamento: "Valle del Cauca", habilitado: false, tiposConTarifaVigente: 0 }),
];

function renderTab(onIrATarifas = vi.fn(), onCambio = vi.fn()) {
  render(
    <ToastProvider>
      <OrganismosYTiposTab onIrATarifas={onIrATarifas} onCambio={onCambio} />
    </ToastProvider>,
  );
  return onIrATarifas;
}
const fila = (re: RegExp) => screen.getByRole("row", { name: re });

describe("OrganismosYTiposTab (#14075)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTiposPn).mockResolvedValue(TIPOS);
    vi.mocked(fetchOrganismosPn).mockResolvedValue(ORGANISMOS);
    vi.mocked(establecerTipoPn).mockResolvedValue(undefined);
    vi.mocked(establecerOrganismoPn).mockResolvedValue(undefined);
  });

  it("AC1: explica la regla en lenguaje claro y muestra las dos tablas con el estilo estándar y paginación", async () => {
    renderTab();
    expect(screen.getByText(/cómo se decide qué ve el ciudadano/i)).toBeInTheDocument();
    expect(screen.getByText(/existe una tarifa vigente para esa combinación/i)).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: /trámites del portal de persona natural/i })).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: /organismos del portal/i })).toBeInTheDocument();
    expect(screen.getAllByText(/filas por página/i).length).toBeGreaterThan(0);
    // Cada sección es un paso numerado y no muestra códigos internos.
    expect(screen.getByRole("heading", { name: /trámites que ofrece el portal/i })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /organismos de tránsito/i })).toBeInTheDocument();
    expect(screen.queryByText("CAMBIO_COLOR")).not.toBeInTheDocument();
    expect(screen.queryByText("BOG")).not.toBeInTheDocument();
  });

  it("avisa que solo las oficinas de tránsito activas pueden recibir trámites", async () => {
    renderTab();
    await screen.findByRole("table", { name: /organismos del portal/i });
    const aviso = screen.getByText(/solo las oficinas de tránsito activas pueden recibir trámites/i).closest("[role='alert']");
    expect(aviso).toHaveTextContent(/FLIT rechazará su solicitud/i);
  });

  it("AC1: un organismo habilitado sin tarifa vigente avisa «Falta tarifa» con botón a Tarifas", async () => {
    const ue = userEvent.setup();
    const onIrATarifas = renderTab();
    await screen.findByRole("table", { name: /organismos del portal/i });
    const medellin = fila(/tránsito de medellín/i);
    expect(within(medellin).getByText("Falta tarifa")).toBeInTheDocument();
    await ue.click(within(medellin).getByRole("button", { name: /crear tarifa para tránsito de medellín/i }));
    expect(onIrATarifas).toHaveBeenCalledTimes(1);
    // El que tiene tarifa y el deshabilitado no avisan.
    expect(within(fila(/secretaría de movilidad de bogotá/i)).queryByText("Falta tarifa")).not.toBeInTheDocument();
    expect(within(fila(/movilidad de cali/i)).queryByText("Falta tarifa")).not.toBeInTheDocument();
  });

  it("AC1: ofrecer un organismo se refleja en la fila; dejar de ofrecerlo pide confirmación antes de llamar a la API", async () => {
    const ue = userEvent.setup();
    const onCambio = vi.fn();
    renderTab(vi.fn(), onCambio);
    await screen.findByRole("table", { name: /organismos del portal/i });

    await ue.click(within(fila(/movilidad de cali/i)).getByRole("switch", { name: /^ofrecer movilidad de cali en el portal/i }));
    await waitFor(() => expect(establecerOrganismoPn).toHaveBeenCalledWith("o3", true));
    await waitFor(() =>
      expect(within(fila(/movilidad de cali/i)).getByRole("switch", { name: /dejar de ofrecer movilidad de cali/i })).toBeChecked(),
    );
    // Recién habilitado y sin tarifa: ahora sí avisa.
    expect(within(fila(/movilidad de cali/i)).getByText("Falta tarifa")).toBeInTheDocument();
    expect(await screen.findByText(/«movilidad de cali» ahora se ofrece en el portal/i)).toBeInTheDocument();
    expect(onCambio).toHaveBeenCalledTimes(1);

    // Apagar: primero la confirmación, y cancelar no toca nada.
    await ue.click(within(fila(/tránsito de medellín/i)).getByRole("switch", { name: /dejar de ofrecer tránsito de medellín/i }));
    const dialogo = await screen.findByRole("dialog", { name: /dejar de ofrecer este organismo/i });
    expect(dialogo).toHaveTextContent(/dejará de aparecer en el portal/i);
    await ue.click(within(dialogo).getByRole("button", { name: /cancelar/i }));
    expect(establecerOrganismoPn).toHaveBeenCalledTimes(1);

    await ue.click(within(fila(/tránsito de medellín/i)).getByRole("switch", { name: /dejar de ofrecer tránsito de medellín/i }));
    await ue.click(within(await screen.findByRole("dialog")).getByRole("button", { name: /^dejar de ofrecer$/i }));
    await waitFor(() => expect(establecerOrganismoPn).toHaveBeenCalledWith("o2", false));
    expect(await screen.findByText(/«tránsito de medellín» ya no se ofrece en el portal/i)).toBeInTheDocument();
  });

  it("AC1: el buscador filtra los ~1100 organismos por nombre, municipio o departamento", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /organismos del portal/i });
    await ue.type(screen.getByRole("textbox", { name: /buscar organismo/i }), "medellin");
    expect(screen.queryByRole("row", { name: /movilidad de cali/i })).not.toBeInTheDocument();
    expect(fila(/tránsito de medellín/i)).toBeInTheDocument();
    await ue.clear(screen.getByRole("textbox", { name: /buscar organismo/i }));
    await ue.type(screen.getByRole("textbox", { name: /buscar organismo/i }), "zzz");
    expect(await screen.findByText(/ningún registro coincide/i)).toBeInTheDocument();
  });

  it("AC1: un trámite activado sin tarifa avisa «Falta tarifa»; uno aún no disponible no se puede activar y lo explica", async () => {
    renderTab();
    await screen.findByRole("table", { name: /trámites del portal de persona natural/i });
    const matricula = fila(/matrícula inicial/i);
    expect(within(matricula).getByText("Falta tarifa")).toBeInTheDocument();
    expect(within(matricula).getByRole("button", { name: /crear tarifa para matrícula inicial/i })).toBeInTheDocument();
    const prenda = fila(/inscripción de prenda/i);
    expect(within(prenda).getByText("Aún no disponible")).toBeInTheDocument();
    expect(within(prenda).getByText(/todavía no está terminado en flit 2/i)).toBeInTheDocument();
    expect(within(prenda).getByRole("switch", { name: /^ofrecer inscripción de prenda/i })).toBeDisabled();
  });

  it("AC1: un 422 tipo_no_elegible muestra el motivo en lenguaje claro y recarga; luego ofrecer un trámite listo actualiza la fila", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /trámites del portal de persona natural/i });

    vi.mocked(establecerTipoPn).mockRejectedValueOnce(new ApiError(422, "tipo_no_elegible", { error: "tipo_no_elegible", message: "x" }));
    const interruptor = screen.getByRole("switch", { name: /^ofrecer cambio de color/i });
    await ue.click(interruptor);
    expect(await within(interruptor.closest("tr") as HTMLElement).findByRole("alert")).toHaveTextContent(/todavía no está listo para ofrecerse/i);
    await waitFor(() => expect(fetchTiposPn).toHaveBeenCalledTimes(2));

    await ue.click(within(fila(/cambio de color/i)).getByRole("switch", { name: /^ofrecer cambio de color/i }));
    await waitFor(() => expect(establecerTipoPn).toHaveBeenLastCalledWith("t4", true));
    await waitFor(() => expect(within(fila(/cambio de color/i)).getByText("Sí")).toBeInTheDocument());
    expect(await screen.findByText(/«cambio de color» ahora se ofrece en el portal/i)).toBeInTheDocument();
  });

  it("dejar de ofrecer un trámite pide confirmación y avisa que el ciudadano ya no podrá iniciarlo", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /trámites del portal de persona natural/i });
    await ue.click(screen.getByRole("switch", { name: /dejar de ofrecer traspaso en el portal/i }));
    const dialogo = await screen.findByRole("dialog", { name: /dejar de ofrecer este trámite/i });
    expect(dialogo).toHaveTextContent(/los ciudadanos no podrán iniciar este trámite/i);
    expect(establecerTipoPn).not.toHaveBeenCalled();
    await ue.click(within(dialogo).getByRole("button", { name: /^dejar de ofrecer$/i }));
    await waitFor(() => expect(establecerTipoPn).toHaveBeenCalledWith("t1", false));
    expect(await screen.findByText(/«traspaso» ya no se ofrece en el portal/i)).toBeInTheDocument();
  });

  it("si falla al dejar de ofrecer, el motivo se ve dentro de la confirmación y no se cierra", async () => {
    const ue = userEvent.setup();
    vi.mocked(establecerTipoPn).mockRejectedValueOnce(new ApiError(500, "x", { error: "x" }));
    renderTab();
    await screen.findByRole("table", { name: /trámites del portal de persona natural/i });
    await ue.click(screen.getByRole("switch", { name: /dejar de ofrecer traspaso en el portal/i }));
    const dialogo = await screen.findByRole("dialog");
    await ue.click(within(dialogo).getByRole("button", { name: /^dejar de ofrecer$/i }));
    expect(await within(dialogo).findByText(/FLIT tuvo un problema al guardar/i)).toBeInTheDocument();
  });

  it("AC5: vacío, error con reintento y cargando", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTiposPn).mockResolvedValueOnce([]);
    vi.mocked(fetchOrganismosPn).mockRejectedValueOnce(new Error("caído"));
    const { unmount } = render(
      <ToastProvider>
        <OrganismosYTiposTab onIrATarifas={vi.fn()} onCambio={vi.fn()} />
      </ToastProvider>,
    );
    expect(await screen.findByText(/todavía no hay trámites listos para persona natural/i)).toBeInTheDocument();
    expect(await screen.findByText(/no se pudieron cargar los organismos/i)).toBeInTheDocument();
    await ue.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByRole("table", { name: /organismos del portal/i })).toBeInTheDocument();
    unmount();

    vi.mocked(fetchTiposPn).mockReturnValue(new Promise(() => {}));
    vi.mocked(fetchOrganismosPn).mockReturnValue(new Promise(() => {}));
    renderTab();
    expect(screen.getAllByText(/cargando/i).length).toBeGreaterThan(0);
  });
});
