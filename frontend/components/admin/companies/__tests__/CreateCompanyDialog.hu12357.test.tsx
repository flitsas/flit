import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CreateCompanyDialog } from "../CreateCompanyDialog";

describe("CreateCompanyDialog (HU #12357)", () => {
  it("SuperAdmin ve los cinco tipos incluyendo Concesión y Marca Blanca", () => {
    render(
      <CreateCompanyDialog
        open
        onClose={vi.fn()}
        onCreate={vi.fn()}
        onCreated={vi.fn()}
        isSuperAdmin
      />,
    );

    const select = screen.getByLabelText(/tipo de compañía/i);
    expect(select).toHaveTextContent("Concesión");
    expect(select).toHaveTextContent("Marca Blanca");
    expect(select).toHaveTextContent("Concesionario de vehículos");
  });

  it("usuario no SuperAdmin no ve Concesión ni Marca Blanca", () => {
    render(
      <CreateCompanyDialog
        open
        onClose={vi.fn()}
        onCreate={vi.fn()}
        onCreated={vi.fn()}
        isSuperAdmin={false}
      />,
    );

    const select = screen.getByLabelText(/tipo de compañía/i);
    expect(select).not.toHaveTextContent("Concesión");
    expect(select).not.toHaveTextContent("Marca Blanca");
    expect(select).toHaveTextContent("Concesionario de vehículos");
  });

  it("al elegir Marca Blanca muestra placeholder de dominio", () => {
    render(
      <CreateCompanyDialog open onClose={vi.fn()} onCreate={vi.fn()} onCreated={vi.fn()} isSuperAdmin />,
    );

    const select = screen.getByLabelText(/tipo de compañía/i) as HTMLSelectElement;
    select.value = "MARCA_BLANCA";
    select.dispatchEvent(new Event("change", { bubbles: true }));

    expect(screen.getByText(/dominio de integración/i)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/pendiente de registro/i)).toBeDisabled();
  });
});
