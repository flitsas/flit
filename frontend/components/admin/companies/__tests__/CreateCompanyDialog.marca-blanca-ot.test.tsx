// Alta de una Marca Blanca: como la Concesión, tras NIT y nombre pasa a elegir sus OT. Su acceso es
// «operables menos bloqueados», así que lo no elegido se bloquea; sin elegir nada no se bloquea nada
// y la red queda con todos los OT activos de FLIT.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CreateCompanyDialog } from "../CreateCompanyDialog";

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: vi.fn(),
  addTransitGrant: vi.fn(),
  addTransitBlock: vi.fn(),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficesOperationalStatus: vi.fn(),
}));

import { addTransitBlock, addTransitGrant, fetchTransitOffices } from "@/lib/api/admin-companies";
import { fetchTransitOfficesOperationalStatus } from "@/lib/api/admin-transit-office-tenants";

const offices = [
  { id: "ot-bog", code: "11001", name: "Secretaría Bogotá", departmentCode: "11", cityCode: "11001" },
  { id: "ot-med", code: "05001", name: "Secretaría Medellín", departmentCode: "05", cityCode: "05001" },
  { id: "ot-cal", code: "76001", name: "Secretaría Cali", departmentCode: "76", cityCode: "76001" },
];

const created = {
  id: "00000000-0000-0000-0000-0000000000mb",
  nit: "900555111-2",
  razonSocial: "Movilidad Andina S.A.S.",
  estadoActivo: true,
  fechaCreacion: "2026-10-20T00:00:00Z",
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(fetchTransitOffices).mockResolvedValue(offices);
  vi.mocked(fetchTransitOfficesOperationalStatus).mockResolvedValue(
    offices.map((o) => ({ id: o.id, hasTenant: true, estadoActivo: true })) as never,
  );
  vi.mocked(addTransitBlock).mockResolvedValue(undefined);
});

async function fillAndContinue(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/razón social/i), created.razonSocial);
  await user.type(screen.getByLabelText(/^nit$/i), created.nit);
  await user.selectOptions(screen.getByLabelText(/tipo de compañía/i), "MARCA_BLANCA");
  await user.click(screen.getByRole("button", { name: /siguiente/i }));
  await screen.findByText(/organismos de tránsito de la marca blanca/i);
}

describe("CreateCompanyDialog — Marca Blanca elige sus OT en el alta", () => {
  it("bloquea los OT que no se eligieron y no usa grants", async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn().mockResolvedValue(created);
    const onCreated = vi.fn();
    render(<CreateCompanyDialog open onClose={vi.fn()} onCreate={onCreate} onCreated={onCreated} isSuperAdmin />);

    await fillAndContinue(user);
    await user.click(screen.getByRole("checkbox", { name: /secretaría bogotá/i }));
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(created));
    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({ tenantType: "MARCA_BLANCA" }));
    expect(addTransitBlock).toHaveBeenCalledTimes(2);
    expect(addTransitBlock).toHaveBeenCalledWith(created.id, "ot-med");
    expect(addTransitBlock).toHaveBeenCalledWith(created.id, "ot-cal");
    expect(addTransitGrant).not.toHaveBeenCalled();
  });

  it("sin elegir ninguno se crea sin bloqueos (todos los OT activos de FLIT)", async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    render(
      <CreateCompanyDialog
        open
        onClose={vi.fn()}
        onCreate={vi.fn().mockResolvedValue(created)}
        onCreated={onCreated}
        isSuperAdmin
      />,
    );

    await fillAndContinue(user);
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(created));
    expect(addTransitBlock).not.toHaveBeenCalled();
  });

  it("si un bloqueo falla, la compañía queda creada y se avisa qué falta", async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    vi.mocked(addTransitBlock).mockImplementation(async (_tenant, otId) => {
      if (otId === "ot-cal") throw new Error("boom");
    });
    render(
      <CreateCompanyDialog
        open
        onClose={vi.fn()}
        onCreate={vi.fn().mockResolvedValue(created)}
        onCreated={onCreated}
        isSuperAdmin
      />,
    );

    await fillAndContinue(user);
    await user.click(screen.getByRole("checkbox", { name: /secretaría bogotá/i }));
    await user.click(screen.getByRole("button", { name: /crear compañía/i }));

    expect(await screen.findByText(/no se pudieron bloquear 1 organismo/i)).toHaveTextContent("Secretaría Cali");
    expect(onCreated).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: /continuar/i }));
    expect(onCreated).toHaveBeenCalledWith(created);
  });
});
