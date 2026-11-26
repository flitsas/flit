import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { CreateCompanyDialog } from "../CreateCompanyDialog";

describe("CreateCompanyDialog (HU #12357)", () => {
  it("SuperAdmin ve las cabezas Concesión y Marca Blanca, pero no Cliente concesión (Bug #14714)", () => {
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
    // Bug #14714: el Cliente concesión solo existe como hijo de una cabeza; no se crea desde el alta general.
    expect(select).not.toHaveTextContent("Cliente concesión");
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
    expect(select).toHaveTextContent("Radicadora");
  });

  it("al elegir Marca Blanca muestra el campo opcional de dominio (HU #12427 AC3)", () => {
    render(
      <CreateCompanyDialog open onClose={vi.fn()} onCreate={vi.fn()} onCreated={vi.fn()} isSuperAdmin />,
    );

    const select = screen.getByLabelText(/tipo de compañía/i) as HTMLSelectElement;
    select.value = "MARCA_BLANCA";
    select.dispatchEvent(new Event("change", { bubbles: true }));

    const domainInput = screen.getByLabelText(/dominio de la red \(opcional\)/i);
    expect(domainInput).toBeEnabled();
    expect(screen.getByText(/puedes dejarlo en blanco/i)).toBeInTheDocument();
  });

  it("otros tipos no muestran el campo de dominio (paridad AC4)", () => {
    render(
      <CreateCompanyDialog open onClose={vi.fn()} onCreate={vi.fn()} onCreated={vi.fn()} isSuperAdmin />,
    );

    expect(screen.queryByLabelText(/dominio de la red/i)).not.toBeInTheDocument();
  });
});
