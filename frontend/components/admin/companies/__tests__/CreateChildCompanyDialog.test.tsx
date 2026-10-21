import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CreateChildCompanyDialog } from "../CreateChildCompanyDialog";

const HEAD = "791a3b49-a77c-45ae-8fba-e2613a56f553";

describe("CreateChildCompanyDialog — tipo de compañía", () => {
  it("en cabeza Concesión deja Cliente concesión seleccionado y deshabilitado", () => {
    render(
      <CreateChildCompanyDialog
        open
        headTenantId={HEAD}
        headTenantType="CONCESION"
        onClose={vi.fn()}
        onCreate={vi.fn()}
        onCreated={vi.fn()}
      />,
    );

    const select = screen.getByLabelText(/tipo de compañía/i) as HTMLSelectElement;
    expect(select).toBeDisabled();
    expect(select.value).toBe("CONCESIONARIO");
    expect(select).toHaveTextContent("Cliente concesión");
    expect(screen.getByText(/fijado según la cabeza de grupo/i)).toBeInTheDocument();
  });

  it("en cabeza Marca Blanca deja Renting seleccionado y deshabilitado", () => {
    render(
      <CreateChildCompanyDialog
        open
        headTenantId={HEAD}
        headTenantType="MARCA_BLANCA"
        onClose={vi.fn()}
        onCreate={vi.fn()}
        onCreated={vi.fn()}
      />,
    );

    const select = screen.getByLabelText(/tipo de compañía/i) as HTMLSelectElement;
    expect(select).toBeDisabled();
    expect(select.value).toBe("RENTING");
    expect(select).toHaveTextContent("Radicadora");
  });

  it("HU #13607: no muestra el campo Código y crea el cliente sin enviarlo", async () => {
    const user = userEvent.setup();
    const onCreate = vi.fn().mockResolvedValue({ id: "c1", code: "COMP-0600" });
    render(
      <CreateChildCompanyDialog
        open
        headTenantId={HEAD}
        headTenantType="CONCESION"
        onClose={vi.fn()}
        onCreate={onCreate}
        onCreated={vi.fn()}
      />,
    );

    expect(screen.queryByLabelText(/código/i)).not.toBeInTheDocument();

    await user.type(screen.getByLabelText(/razón social/i), "Cliente Hijo S.A.S.");
    await user.type(screen.getByLabelText(/^nit$/i), "900100200-1");
    await user.click(screen.getByRole("button", { name: /crear cliente/i }));

    await waitFor(() => expect(onCreate).toHaveBeenCalled());
    expect(onCreate.mock.calls[0][0]).toBe(HEAD);
    expect(onCreate.mock.calls[0][1]).not.toHaveProperty("code");
  });
});
