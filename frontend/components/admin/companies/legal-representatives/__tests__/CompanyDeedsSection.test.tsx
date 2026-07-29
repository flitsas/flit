// HU #11063 — Escrituras por compañía como sección propia: panorama de vigencia y carga/reemplazo en
// un paso, sin entrar al detalle de un representante. El backend ya soportaba todo; lo que cambia es
// el recorrido.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { CompanyDeedsSection } from "../CompanyDeedsSection";
import type { DeedItem, DeedPage, RepresentedCompany } from "@/lib/api/admin-deeds";

vi.mock("@/lib/api/admin-deeds", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-deeds")>();
  return {
    ...actual,
    fetchDeeds: vi.fn(),
    fetchRepresentedCompanies: vi.fn(),
    fetchDeedDetail: vi.fn(),
    saveDeed: vi.fn(),
  };
});

import {
  fetchDeedDetail,
  fetchDeeds,
  fetchRepresentedCompanies,
} from "@/lib/api/admin-deeds";

const TENANT = "11111111-1111-1111-1111-111111111111";

const COMPANIES: RepresentedCompany[] = [
  { id: "co-1", nit: "900123456-7", name: "Comercializadora XYZ" },
  { id: "co-2", nit: "901987654-3", name: "Inversiones ABC" },
];

/** Fechas relativas a hoy para que la vigencia no dependa del día en que corran las pruebas. */
function iso(offsetDias: number): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDias);
  return d.toISOString().slice(0, 10);
}

function deed(over: Partial<DeedItem> = {}): DeedItem {
  return {
    id: "deed-1",
    description: "Escritura 123 de 2026",
    storagePath: "deeds/deed-1.pdf",
    storageSha256: "abc",
    vigenciaDesde: iso(-30),
    vigenciaHasta: iso(180),
    isActive: true,
    representedCompanyIds: ["co-1"],
    createdAt: "2026-06-01T00:00:00Z",
    updatedAt: null,
    ...over,
  };
}

function page(data: DeedItem[]): DeedPage {
  return { data, totalCount: data.length, page: 1, pageSize: 200 };
}

function renderSection() {
  return render(
    <ToastProvider>
      <CompanyDeedsSection tenantId={TENANT} />
    </ToastProvider>,
  );
}

describe("CompanyDeedsSection (HU #11063)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchRepresentedCompanies).mockResolvedValue(COMPANIES);
  });

  it("lista cada compañía con su escritura y los días restantes de vigencia", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([deed()]));
    renderSection();

    expect(await screen.findByText("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByText("Escritura 123 de 2026")).toBeInTheDocument();
    expect(screen.getByText("Vigente")).toBeInTheDocument();
    expect(screen.getByText("180 días")).toBeInTheDocument();
  });

  it("marca como vencida la escritura fuera de vigencia y no muestra días restantes", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(
      page([deed({ vigenciaDesde: iso(-400), vigenciaHasta: iso(-10) })]),
    );
    renderSection();

    expect(await screen.findByText("Vencida")).toBeInTheDocument();
    // Un contador negativo no significa nada para el gestor.
    expect(screen.queryByText(/-\d+ días/)).not.toBeInTheDocument();
  });

  it("una compañía sin escritura se lista igual, con acceso directo a cargarla", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([deed()]));
    renderSection();

    expect(await screen.findByText("Inversiones ABC")).toBeInTheDocument();
    expect(screen.getByText("Sin escritura registrada")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Cargar escritura de Inversiones ABC" }),
    ).toBeInTheDocument();
  });

  it("cargar desde la fila abre el formulario con esa compañía fija, sin pedirla otra vez", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([deed()]));
    renderSection();

    await screen.findByText("Inversiones ABC");
    await userEvent.click(
      screen.getByRole("button", { name: "Cargar escritura de Inversiones ABC" }),
    );

    // El panel muestra la compañía como dato de solo lectura (no hay selector que equivocar).
    const panel = await screen.findByRole("dialog");
    expect(within(panel).getByText(/Inversiones ABC/)).toBeInTheDocument();
  });

  it("con varias compañías el alta general pregunta primero de cuál es", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([deed()]));
    renderSection();

    await screen.findByText("Comercializadora XYZ");
    await userEvent.click(screen.getByRole("button", { name: /cargar escritura$/i }));

    const picker = await screen.findByRole("dialog");
    expect(within(picker).getByText(/de qué compañía es la escritura/i)).toBeInTheDocument();
    expect(within(picker).getByText("Inversiones ABC")).toBeInTheDocument();
  });

  it("ver la escritura abre el PDF con la URL prefirmada", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([deed()]));
    vi.mocked(fetchDeedDetail).mockResolvedValue({
      deed: deed(),
      viewUrl: "https://s3.local/deed-1.pdf",
      viewUrlExpiresAt: null,
    });
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    renderSection();

    await screen.findByText("Escritura 123 de 2026");
    await userEvent.click(
      screen.getByRole("button", { name: "Ver la escritura de Comercializadora XYZ" }),
    );

    await waitFor(() =>
      expect(open).toHaveBeenCalledWith(
        "https://s3.local/deed-1.pdf",
        "_blank",
        "noopener,noreferrer",
      ),
    );
    open.mockRestore();
  });

  it("una escritura que cubre varias compañías aparece en cada una", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(
      page([deed({ representedCompanyIds: ["co-1", "co-2"] })]),
    );
    renderSection();

    await screen.findByText("Comercializadora XYZ");
    expect(screen.getAllByText("Escritura 123 de 2026")).toHaveLength(2);
    expect(screen.queryByText("Sin escritura registrada")).not.toBeInTheDocument();
  });

  it("muestra el error y permite reintentar", async () => {
    vi.mocked(fetchDeeds).mockRejectedValueOnce(new Error("boom"));
    renderSection();

    expect(await screen.findByText(/no se pudieron cargar las escrituras/i)).toBeInTheDocument();

    vi.mocked(fetchDeeds).mockResolvedValueOnce(page([deed()]));
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("Escritura 123 de 2026")).toBeInTheDocument();
  });
});
