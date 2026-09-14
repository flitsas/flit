import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
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
    expect(select).toHaveTextContent("Renting");
  });
});
