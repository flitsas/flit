// HU #10905 — Pestaña Escrituras: lista paginada (4 estados UI), estado de vigencia con StatusBadge,
// "Ver PDF" que abre la URL presignada y eliminación con confirmación. La API se mockea.
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { DeedsTab } from "../DeedsTab";
import type { DeedItem, DeedPage, RepresentedCompany } from "@/lib/api/admin-deeds";

vi.mock("@/lib/api/admin-deeds", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/api/admin-deeds")>();
  return {
    ...actual,
    fetchDeeds: vi.fn(),
    fetchDeedDetail: vi.fn(),
    fetchRepresentedCompanies: vi.fn(),
    deleteDeed: vi.fn(),
    saveDeed: vi.fn(),
  };
});

import {
  deleteDeed,
  fetchDeedDetail,
  fetchDeeds,
  fetchRepresentedCompanies,
} from "@/lib/api/admin-deeds";

const TENANT = "11111111-1111-1111-1111-111111111111";

const COMPANY: RepresentedCompany = {
  id: "co-1",
  nit: "900123456-7",
  name: "Comercializadora XYZ",
};

const ITEM: DeedItem = {
  id: "deed-1",
  description: "Escritura de constitución",
  storagePath: "deeds/deed-1.pdf",
  storageSha256: "abc123",
  vigenciaDesde: "2020-01-01",
  vigenciaHasta: "2999-12-31",
  isActive: true,
  representedCompanyIds: ["co-1"],
  createdAt: "2026-06-01T00:00:00Z",
  updatedAt: null,
};

function page(data: DeedItem[]): DeedPage {
  return { data, totalCount: data.length, page: 1, pageSize: 20 };
}

function renderTab() {
  return render(
    <ToastProvider>
      <DeedsTab tenantId={TENANT} />
    </ToastProvider>,
  );
}

describe("DeedsTab (HU #10905)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchRepresentedCompanies).mockResolvedValue([COMPANY]);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("muestra el estado vacío cuando no hay escrituras", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([]));
    renderTab();
    expect(
      await screen.findByText(/aún no tiene escrituras cargadas/i),
    ).toBeInTheDocument();
  });

  it("muestra el estado de error y permite reintentar", async () => {
    vi.mocked(fetchDeeds).mockRejectedValueOnce(new Error("boom"));
    renderTab();
    expect(await screen.findByText(/no se pudieron cargar las escrituras/i)).toBeInTheDocument();

    vi.mocked(fetchDeeds).mockResolvedValueOnce(page([ITEM]));
    await userEvent.click(screen.getByRole("button", { name: /reintentar/i }));
    expect(await screen.findByText("Escritura de constitución")).toBeInTheDocument();
  });

  it("lista con el nombre de la compañía y el estado de vigencia", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([ITEM]));
    renderTab();
    expect(await screen.findByText("Escritura de constitución")).toBeInTheDocument();
    expect(screen.getByText("Comercializadora XYZ")).toBeInTheDocument();
    expect(screen.getByText(/Vigente/i)).toBeInTheDocument();
  });

  it("abre la URL presignada del PDF al pulsar 'Ver PDF'", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([ITEM]));
    vi.mocked(fetchDeedDetail).mockResolvedValue({
      deed: ITEM,
      viewUrl: "https://s3.example/deeds/deed-1.pdf?sig=xyz",
      viewUrlExpiresAt: "2026-07-23T00:10:00Z",
    });
    const open = vi.spyOn(window, "open").mockReturnValue(null);
    renderTab();
    await screen.findByText("Escritura de constitución");

    await userEvent.click(screen.getByRole("button", { name: /ver pdf de escritura de constitución/i }));
    await waitFor(() =>
      expect(open).toHaveBeenCalledWith(
        "https://s3.example/deeds/deed-1.pdf?sig=xyz",
        "_blank",
        "noopener,noreferrer",
      ),
    );
  });

  it("elimina una escritura tras confirmar en el diálogo", async () => {
    vi.mocked(fetchDeeds).mockResolvedValue(page([ITEM]));
    vi.mocked(deleteDeed).mockResolvedValue(undefined);
    renderTab();
    await screen.findByText("Escritura de constitución");

    await userEvent.click(screen.getByRole("button", { name: /eliminar escritura de constitución/i }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: /^eliminar$/i }));

    await waitFor(() => expect(deleteDeed).toHaveBeenCalledWith(TENANT, "deed-1"));
  });
});
