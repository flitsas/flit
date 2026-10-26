// HU #14003 — filtros por columna en «Organismos de tránsito» (OTConfigTablePanel) y «Bloqueos»
// (TransitBlocksPanel editable y TransitBlocksReadOnlySection). Usa la barra genérica de la HU #13997.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { OTConfigTablePanel } from "../OTConfigTablePanel";
import { TransitBlocksPanel } from "../TransitBlocksPanel";
import { TransitBlocksReadOnlySection } from "../TransitBlocksReadOnlySection";
import type { TransitOffice } from "@/lib/api/types";
import type { TransitOfficeOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  fetchTransitGrants: vi.fn(),
  addTransitGrant: vi.fn(),
  removeTransitGrant: vi.fn(),
  fetchOtBlockingPolicies: vi.fn(() => Promise.resolve([])),
  setOtBlockingPolicy: vi.fn(),
  fetchOtConsultationRestrictions: vi.fn(() => Promise.resolve([])),
  setOtConsultationRestriction: vi.fn(),
  fetchTransitAgreements: vi.fn(),
  setTransitAgreement: vi.fn(),
  fetchOtPrendaDocumentPolicies: vi.fn(() => Promise.resolve([])),
  setOtPrendaDocumentPolicy: vi.fn(),
  fetchTransitBlocks: vi.fn(),
  addTransitBlock: vi.fn(),
  removeTransitBlock: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

vi.mock("@/components/admin/Toast", () => ({
  useToast: () => ({ show: vi.fn() }),
}));

import {
  addTransitBlock,
  addTransitGrant,
  fetchTransitAgreements,
  fetchTransitBlocks,
  fetchTransitGrants,
  fetchTransitOffices,
  setTransitAgreement,
} from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const TENANT = "t-1";

const offices: TransitOffice[] = [
  { id: "o1", code: "11001", name: "Secretaría Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "o2", code: "05001", name: "Secretaría Medellín", departmentCode: "05", cityCode: "05001" },
  { id: "o3", code: "76001", name: "Tránsito Cali", departmentCode: "76", cityCode: "76001" },
];

function activos(): TransitOfficeOperationalStatus[] {
  return offices.map((o) => ({
    id: o.id,
    code: o.code,
    name: o.name,
    departmentCode: o.departmentCode,
    hasTenant: true,
    tenantId: `tenant-${o.id}`,
    estadoActivo: true,
    operationMode: null,
    divipoCode: null,
    quipuxRegistration: false,
    quipuxTransfer: false,
    quipuxOther: false,
  }));
}

/** «+ Filtro» → columna → valor → Aplicar. `valor` string = texto; string[] = opciones a marcar. */
async function aplicarFiltro(
  user: ReturnType<typeof userEvent.setup>,
  columna: string,
  valor: string | string[],
) {
  await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
  const dialog = await screen.findByRole("dialog", { name: /filtros de la tabla/i });
  await user.click(within(dialog).getByRole("button", { name: columna }));
  if (typeof valor === "string") {
    await user.type(within(dialog).getByLabelText(new RegExp(`${columna} contiene`, "i")), valor);
  } else {
    for (const opcion of valor) {
      await user.click(within(dialog).getByLabelText(opcion));
    }
  }
  await user.click(within(dialog).getByRole("button", { name: /^aplicar$/i }));
}

async function columnasFiltrables(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /\+ filtro/i }));
  const lista = within(await screen.findByRole("list", { name: /columnas filtrables/i }));
  return lista.queryAllByRole("button").map((b) => b.textContent);
}

function arrangeOt() {
  vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  // o1 habilitado y con convenio; o2 habilitado sin convenio; o3 sin ninguno.
  vi.mocked(fetchTransitGrants).mockResolvedValue({ transitOfficeIds: ["o1", "o2"] });
  vi.mocked(fetchTransitAgreements).mockResolvedValue({ transitOfficeIds: ["o1"] });
  vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: [] });
  vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(activos());
}

describe("HU #14003 AC1 — Organismos de tránsito: filtros por columna", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    arrangeOt();
  });

  it("ofrece Organismo, Código, Estado y Convenio, y nunca Acciones", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    expect(await columnasFiltrables(user)).toEqual(["Organismo", "Código", "Estado", "Convenio"]);
  });

  it("filtra por Organismo", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Organismo", "medellin");
    expect(await screen.findByText("Secretaría Medellín")).toBeInTheDocument();
    expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument();
    expect(screen.queryByText("Tránsito Cali")).not.toBeInTheDocument();
  });

  it("filtra por Código", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Código", "76001");
    expect(await screen.findByText("Tránsito Cali")).toBeInTheDocument();
    expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument();
  });

  it("filtra por Estado y por Convenio con AND entre ambos", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");

    await aplicarFiltro(user, "Estado", ["Habilitado"]);
    await waitFor(() => expect(screen.queryByText("Tránsito Cali")).not.toBeInTheDocument());
    expect(screen.getByText("Secretaría Bogotá")).toBeInTheDocument();
    expect(screen.getByText("Secretaría Medellín")).toBeInTheDocument();

    await aplicarFiltro(user, "Convenio", ["Con convenio"]);
    await waitFor(() => expect(screen.queryByText("Secretaría Medellín")).not.toBeInTheDocument());
    expect(screen.getByText("Secretaría Bogotá")).toBeInTheDocument();
  });

  it("modo solo lectura: sin columna Convenio, tampoco su filtro", async () => {
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} mode="readonly-concession-inherited" />);
    await screen.findByText("Secretaría Bogotá");
    expect(await columnasFiltrables(user)).toEqual(["Organismo", "Código", "Estado"]);
  });
});

describe("HU #14003 AC3 — acciones con filtros aplicados (Organismos de tránsito)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    arrangeOt();
  });

  it("habilitar y cambiar convenio de un organismo visible guarda, actualiza la fila y conserva el filtro", async () => {
    const user = userEvent.setup();
    vi.mocked(addTransitGrant).mockResolvedValue(undefined);
    vi.mocked(setTransitAgreement).mockResolvedValue(undefined);
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Tránsito Cali");
    await aplicarFiltro(user, "Organismo", "cali");
    await waitFor(() => expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument());

    await user.click(screen.getByRole("switch", { name: /habilitar tránsito cali/i }));
    await waitFor(() => expect(addTransitGrant).toHaveBeenCalledWith(TENANT, "o3"));
    expect(
      await screen.findByRole("switch", { name: /deshabilitar tránsito cali/i }),
    ).toBeChecked();

    await user.click(screen.getByRole("switch", { name: /marcar convenio con tránsito cali/i }));
    await waitFor(() => expect(setTransitAgreement).toHaveBeenCalledWith(TENANT, "o3", true));
    expect(
      await screen.findByRole("switch", { name: /quitar convenio con tránsito cali/i }),
    ).toBeChecked();

    // El filtro sigue aplicado: contador en el disparador y las demás filas siguen fuera.
    expect(screen.getByRole("button", { name: /\+ filtro \(1\)/i })).toBeInTheDocument();
    expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument();
  });
});

describe("HU #14003 AC4 — sin resultados (Organismos de tránsito)", () => {
  it("muestra el estado vacío con «Limpiar filtros» que restaura la lista", async () => {
    vi.clearAllMocks();
    arrangeOt();
    const user = userEvent.setup();
    render(<OTConfigTablePanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Organismo", "zzzz");

    expect(
      await screen.findByText(/ningún registro coincide con los filtros/i),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /limpiar filtros/i }));
    expect(await screen.findByText("Secretaría Bogotá")).toBeInTheDocument();
    expect(screen.getByText("Tránsito Cali")).toBeInTheDocument();
  });
});

describe("HU #14003 AC2/AC3/AC4 — Bloqueos editable (TransitBlocksPanel)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
    vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: ["o2"] });
    vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(activos());
  });

  it("ofrece Organismo, Código y Estado en red", async () => {
    const user = userEvent.setup();
    render(<TransitBlocksPanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    expect(await columnasFiltrables(user)).toEqual(["Organismo", "Código", "Estado en red"]);
  });

  it("filtra por Organismo y por Código", async () => {
    const user = userEvent.setup();
    render(<TransitBlocksPanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Organismo", "secretaria");
    await waitFor(() => expect(screen.queryByText("Tránsito Cali")).not.toBeInTheDocument());
    await aplicarFiltro(user, "Código", "05001");
    await waitFor(() => expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument());
    expect(screen.getByText("Secretaría Medellín")).toBeInTheDocument();
  });

  it("filtra por Estado en red y combina con Organismo (AND)", async () => {
    const user = userEvent.setup();
    render(<TransitBlocksPanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Estado en red", ["Disponible"]);
    await waitFor(() => expect(screen.queryByText("Secretaría Medellín")).not.toBeInTheDocument());
    expect(screen.getByText("Secretaría Bogotá")).toBeInTheDocument();
    await aplicarFiltro(user, "Organismo", "cali");
    await waitFor(() => expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument());
    expect(screen.getByText("Tránsito Cali")).toBeInTheDocument();
  });

  it("AC3 — bloquear un organismo visible guarda, actualiza la fila y conserva el filtro", async () => {
    const user = userEvent.setup();
    vi.mocked(addTransitBlock).mockResolvedValue(undefined);
    render(<TransitBlocksPanel tenantId={TENANT} />);
    await screen.findByText("Tránsito Cali");
    await aplicarFiltro(user, "Organismo", "cali");
    await waitFor(() => expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /bloquear tránsito cali/i }));
    await waitFor(() => expect(addTransitBlock).toHaveBeenCalledWith(TENANT, "o3"));
    expect(await screen.findByText("Bloqueado")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /desbloquear tránsito cali/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /\+ filtro \(1\)/i })).toBeInTheDocument();
    expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument();
  });

  it("AC4 — sin resultados muestra «Limpiar filtros»", async () => {
    const user = userEvent.setup();
    render(<TransitBlocksPanel tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Organismo", "zzzz");
    expect(
      await screen.findByText(/ningún registro coincide con los filtros/i),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /limpiar filtros/i }));
    expect(await screen.findByText("Secretaría Bogotá")).toBeInTheDocument();
  });
});

describe("HU #14003 AC2/AC4 — Bloqueos solo lectura (TransitBlocksReadOnlySection)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
    vi.mocked(fetchTransitBlocks).mockResolvedValue({ transitOfficeIds: ["o1", "o2"] });
  });

  it("filtra por Organismo bloqueado, Código y Estado", async () => {
    const user = userEvent.setup();
    render(<TransitBlocksReadOnlySection tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");

    expect(await columnasFiltrables(user)).toEqual(["Organismo bloqueado", "Código", "Estado"]);
    await user.keyboard("{Escape}");

    await aplicarFiltro(user, "Código", "05001");
    await waitFor(() => expect(screen.queryByText("Secretaría Bogotá")).not.toBeInTheDocument());
    expect(screen.getByText("Secretaría Medellín")).toBeInTheDocument();

    await aplicarFiltro(user, "Estado", ["Bloqueado"]);
    expect(await screen.findByText("Secretaría Medellín")).toBeInTheDocument();
  });

  it("sin resultados muestra «Limpiar filtros»", async () => {
    const user = userEvent.setup();
    render(<TransitBlocksReadOnlySection tenantId={TENANT} />);
    await screen.findByText("Secretaría Bogotá");
    await aplicarFiltro(user, "Organismo bloqueado", "zzzz");
    expect(
      await screen.findByText(/ningún registro coincide con los filtros/i),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /limpiar filtros/i }));
    expect(await screen.findByText("Secretaría Bogotá")).toBeInTheDocument();
  });
});
