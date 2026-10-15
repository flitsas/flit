// HU #13607 — el alta de tenant OT ya no pide el código: lo asigna el servidor (consecutivo COMP-XXXX).
//
// Uso de ejemplo:
//   render(<CreateTransitOfficeTenantDialog open onClose={fn} onCreated={fn} initialOfficeId="office-1" />);
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CreateTransitOfficeTenantDialog } from "../CreateTransitOfficeTenantDialog";

const fetchTransitOffices = vi.fn();
const fetchTransitOfficeTenants = vi.fn();
const createTransitOfficeTenant = vi.fn();

vi.mock("@/lib/api/admin-companies", () => ({
  fetchTransitOffices: (...args: unknown[]) => fetchTransitOffices(...args),
}));

vi.mock("@/lib/api/admin-transit-office-tenants", () => ({
  fetchTransitOfficeTenants: (...args: unknown[]) => fetchTransitOfficeTenants(...args),
  createTransitOfficeTenant: (...args: unknown[]) => createTransitOfficeTenant(...args),
}));

const OFFICE = { id: "office-1", code: "5631000", name: "Tránsito de Sabaneta", departmentCode: "05", cityCode: "05631" };

describe("CreateTransitOfficeTenantDialog (HU #13607)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    fetchTransitOffices.mockResolvedValue([OFFICE]);
    fetchTransitOfficeTenants.mockResolvedValue({ data: [], totalCount: 0, page: 1, pageSize: 100 });
  });

  it("no muestra el campo Código y crea el tenant sin enviarlo", async () => {
    const user = userEvent.setup();
    const onCreated = vi.fn();
    createTransitOfficeTenant.mockResolvedValue({ id: "t1", transitOfficeId: OFFICE.id, code: "COMP-0601" });

    render(<CreateTransitOfficeTenantDialog open onClose={vi.fn()} onCreated={onCreated} initialOfficeId={OFFICE.id} />);

    await screen.findByRole("option", { name: new RegExp(OFFICE.name) });
    expect(screen.queryByLabelText(/código/i)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/razón social/i), "Secretaría de Tránsito de Sabaneta");
    await user.type(screen.getByLabelText(/^nit$/i), "900273813-7");
    await user.click(screen.getByRole("button", { name: /activar organismo de tránsito/i }));

    await waitFor(() => expect(createTransitOfficeTenant).toHaveBeenCalled());
    expect(createTransitOfficeTenant.mock.calls[0][0]).toEqual({
      transitOfficeId: OFFICE.id,
      legalName: "Secretaría de Tránsito de Sabaneta",
      taxId: "900273813-7",
    });
    expect(onCreated).toHaveBeenCalled();
  });

  it("un error sin campo se muestra como alerta", async () => {
    const user = userEvent.setup();
    createTransitOfficeTenant.mockRejectedValue(new Error("red caída"));

    render(<CreateTransitOfficeTenantDialog open onClose={vi.fn()} onCreated={vi.fn()} initialOfficeId={OFFICE.id} />);

    await screen.findByRole("option", { name: new RegExp(OFFICE.name) });
    await user.type(screen.getByLabelText(/razón social/i), "Secretaría de Tránsito de Sabaneta");
    await user.type(screen.getByLabelText(/^nit$/i), "900273813-7");
    await user.click(screen.getByRole("button", { name: /activar organismo de tránsito/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no se pudo crear el organismo de tránsito/i);
  });
});
