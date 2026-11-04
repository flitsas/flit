import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ToastProvider } from "@/components/admin/Toast";
import { OrganismosSection } from "../OrganismosSection";
import { establecerCanalPuntoPn, establecerOrganismoPn, fetchOrganismosPn, type OrganismoPn } from "@/lib/api/admin-pn-config";

vi.mock("@/lib/api/admin-pn-config", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/admin-pn-config")>();
  return {
    ...real,
    fetchOrganismosPn: vi.fn(),
    establecerOrganismoPn: vi.fn(),
    establecerCanalPuntoPn: vi.fn(),
  };
});

const organismo = (over: Partial<OrganismoPn>): OrganismoPn => ({
  id: "o1",
  codigo: "BOG",
  nombre: "Secretaría de Movilidad de Bogotá",
  municipio: "Bogotá",
  departamento: "Bogotá D.C.",
  habilitado: true,
  canalPuntoEncendido: false,
  tiposConTarifaVigente: 2,
  ...over,
});

const ORIGEN = "https://personanatural.flit.test";
const DIRECCION = `${ORIGEN}/o/BOG`;

function renderSeccion() {
  render(
    <ToastProvider>
      <OrganismosSection onIrATarifas={vi.fn()} />
    </ToastProvider>,
  );
}

describe("canal del punto en organismos (#14217)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubEnv("NEXT_PUBLIC_CANAL_OT_ORIGEN", ORIGEN);
    vi.mocked(fetchOrganismosPn).mockResolvedValue([
      organismo({ canalPuntoEncendido: true }),
      organismo({
        id: "o3",
        codigo: "CAL",
        nombre: "Movilidad de Cali",
        municipio: "Cali",
        departamento: "Valle del Cauca",
        habilitado: false,
        canalPuntoEncendido: false,
        tiposConTarifaVigente: 0,
      }),
    ]);
    vi.mocked(establecerOrganismoPn).mockResolvedValue(undefined);
    vi.mocked(establecerCanalPuntoPn).mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it("con el canal apagado no muestra Enlace y QR", async () => {
    renderSeccion();
    const tabla = await screen.findByRole("table", { name: /organismos del portal/i });
    const cali = within(tabla).getByRole("row", { name: /movilidad de cali/i });
    expect(within(cali).queryByRole("button", { name: /enlace y qr/i })).not.toBeInTheDocument();
    expect(within(cali).getByText("Apagado")).toBeInTheDocument();
  });

  it("con el canal encendido, copiar y el QR usan la misma dirección", async () => {
    const ue = userEvent.setup();
    renderSeccion();
    const tabla = await screen.findByRole("table", { name: /organismos del portal/i });
    const bogota = within(tabla).getByRole("row", { name: /secretaría de movilidad de bogotá/i });
    await ue.click(within(bogota).getByRole("button", { name: /enlace y qr de secretaría de movilidad de bogotá/i }));

    const dialogo = await screen.findByRole("dialog", { name: "Enlace y QR" });
    expect(within(dialogo).getByLabelText("Dirección del canal")).toHaveValue(DIRECCION);
    expect(within(dialogo).getByRole("img", { name: `Código QR de ${DIRECCION}` })).toBeInTheDocument();
    expect(DIRECCION).not.toMatch(/placa|documento|\?/);
    expect(within(dialogo).getByRole("button", { name: "Descargar QR" })).toBeInTheDocument();

    const writeText = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue(undefined);
    await ue.click(within(dialogo).getByRole("button", { name: "Copiar enlace" }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(DIRECCION));
    expect(within(dialogo).getByRole("status")).toHaveTextContent("Enlace copiado.");
  });

  it("encender el canal llama al servicio y entonces aparece Enlace y QR", async () => {
    const ue = userEvent.setup();
    renderSeccion();
    const tabla = await screen.findByRole("table", { name: /organismos del portal/i });
    const cali = () => within(tabla).getByRole("row", { name: /movilidad de cali/i });
    await ue.click(within(cali()).getByRole("switch", { name: /encender el canal del punto de movilidad de cali/i }));
    await waitFor(() => expect(establecerCanalPuntoPn).toHaveBeenCalledWith("o3", true));
    expect(await within(cali()).findByRole("button", { name: /enlace y qr/i })).toBeInTheDocument();
  });

  it("conserva el buscador y el filtro de ofrecer en el portal", async () => {
    renderSeccion();
    await screen.findByRole("table", { name: /organismos del portal/i });
    expect(screen.getByRole("textbox", { name: /buscar organismo/i })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: /ofrecer en el portal/i })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: /canal del punto/i })).toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: /municipio/i })).toBeInTheDocument();
  });
});
