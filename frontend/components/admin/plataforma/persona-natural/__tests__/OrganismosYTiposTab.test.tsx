// HU #14075 — Pestaña «Organismos y tipos»: habilitar y deshabilitar con la tabla estándar (AC1), aviso «Sin tarifa
// vigente» con enlace a Tarifas, 422 `tipo_no_elegible`, y los cuatro estados (AC5). La API (HU #14072) es un doble.
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

function renderTab(onIrATarifas = vi.fn()) {
  render(
    <ToastProvider>
      <OrganismosYTiposTab onIrATarifas={onIrATarifas} />
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

  it("AC1: muestra la regla visible y las dos tablas con el estilo estándar y paginación", async () => {
    renderTab();
    expect(screen.getByText(/regla: un trámite aparece en el portal/i)).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: /tipos de trámite del portal/i })).toBeInTheDocument();
    expect(await screen.findByRole("table", { name: /organismos del portal/i })).toBeInTheDocument();
    expect(screen.getAllByText(/filas por página/i).length).toBeGreaterThan(0);
  });

  it("AC1: un organismo habilitado sin tarifa vigente avisa «Sin tarifa vigente» con enlace a Tarifas", async () => {
    const ue = userEvent.setup();
    const onIrATarifas = renderTab();
    await screen.findByRole("table", { name: /organismos del portal/i });
    const medellin = fila(/tránsito de medellín/i);
    expect(within(medellin).getByText("Sin tarifa vigente")).toBeInTheDocument();
    await ue.click(within(medellin).getByRole("button", { name: "Ir a Tarifas" }));
    expect(onIrATarifas).toHaveBeenCalledTimes(1);
    // El que tiene tarifa y el deshabilitado no avisan.
    expect(within(fila(/secretaría de movilidad de bogotá/i)).queryByText("Sin tarifa vigente")).not.toBeInTheDocument();
    expect(within(fila(/movilidad de cali/i)).queryByText("Sin tarifa vigente")).not.toBeInTheDocument();
  });

  it("AC1: habilitar y deshabilitar un organismo se refleja en la fila y confirma con un mensaje", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /organismos del portal/i });

    await ue.click(within(fila(/movilidad de cali/i)).getByRole("switch", { name: /habilitar movilidad de cali en el portal/i }));
    await waitFor(() => expect(establecerOrganismoPn).toHaveBeenCalledWith("o3", true));
    await waitFor(() =>
      expect(within(fila(/movilidad de cali/i)).getByRole("switch", { name: /deshabilitar movilidad de cali/i })).toBeChecked(),
    );
    // Recién habilitado y sin tarifa: ahora sí avisa.
    expect(within(fila(/movilidad de cali/i)).getByText("Sin tarifa vigente")).toBeInTheDocument();
    expect(await screen.findByText(/«movilidad de cali» habilitado para el portal/i)).toBeInTheDocument();

    await ue.click(within(fila(/tránsito de medellín/i)).getByRole("switch", { name: /deshabilitar tránsito de medellín/i }));
    await waitFor(() => expect(establecerOrganismoPn).toHaveBeenCalledWith("o2", false));
  });

  it("AC1: el buscador filtra los ~1100 organismos por nombre, código o municipio", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /organismos del portal/i });
    await ue.type(screen.getByRole("textbox", { name: /buscar organismos/i }), "medellin");
    expect(screen.queryByRole("row", { name: /movilidad de cali/i })).not.toBeInTheDocument();
    expect(fila(/tránsito de medellín/i)).toBeInTheDocument();
    await ue.clear(screen.getByRole("textbox", { name: /buscar organismos/i }));
    await ue.type(screen.getByRole("textbox", { name: /buscar organismos/i }), "zzz");
    expect(await screen.findByText(/ningún registro coincide/i)).toBeInTheDocument();
  });

  it("AC1: un tipo habilitado sin tarifa en ningún organismo avisa «Sin tarifa vigente»; un no elegible no se puede habilitar", async () => {
    renderTab();
    await screen.findByRole("table", { name: /tipos de trámite del portal/i });
    expect(within(fila(/matrícula inicial/i)).getByText("Sin tarifa vigente")).toBeInTheDocument();
    const prenda = fila(/inscripción de prenda/i);
    expect(within(prenda).getByText("No elegible")).toBeInTheDocument();
    expect(within(prenda).getByText("El asistente no está publicado")).toBeInTheDocument();
    expect(within(prenda).getByRole("switch", { name: /habilitar inscripción de prenda/i })).toBeDisabled();
  });

  it("AC1: habilitar un tipo elegible actualiza la fila; un 422 tipo_no_elegible muestra el motivo y recarga", async () => {
    const ue = userEvent.setup();
    renderTab();
    await screen.findByRole("table", { name: /tipos de trámite del portal/i });

    await ue.click(within(fila(/cambio de color/i)).getByRole("switch", { name: /habilitar cambio de color/i }));
    await waitFor(() => expect(establecerTipoPn).toHaveBeenCalledWith("t4", true));
    await waitFor(() => expect(within(fila(/cambio de color/i)).getByText("Se ofrece")).toBeInTheDocument());

    vi.mocked(establecerTipoPn).mockRejectedValueOnce(new ApiError(422, "tipo_no_elegible", { error: "tipo_no_elegible", message: "x" }));
    const interruptor = screen.getByRole("switch", { name: /deshabilitar traspaso en el portal/i });
    await ue.click(interruptor);
    expect(await within(interruptor.closest("tr") as HTMLElement).findByRole("alert")).toHaveTextContent(/no es elegible para persona natural/i);
    await waitFor(() => expect(fetchTiposPn).toHaveBeenCalledTimes(2));
  });

  it("AC5: vacío, error con reintento y cargando", async () => {
    const ue = userEvent.setup();
    vi.mocked(fetchTiposPn).mockResolvedValueOnce([]);
    vi.mocked(fetchOrganismosPn).mockRejectedValueOnce(new Error("caído"));
    const { unmount } = render(
      <ToastProvider>
        <OrganismosYTiposTab onIrATarifas={vi.fn()} />
      </ToastProvider>,
    );
    expect(await screen.findByText(/no hay tipos de trámite elegibles/i)).toBeInTheDocument();
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
