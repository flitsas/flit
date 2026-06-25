// Edición de compañía (#10118) — el modal prellena los datos, deja el Código de solo
// lectura, valida requeridos en cliente, envía el payload correcto vía onUpdate y mapea
// los errores 422 del backend por campo.
//
// Uso de ejemplo:
//   render(<EditCompanyDialog open company={c} onClose={fn} onUpdate={fn} onUpdated={fn} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EditCompanyDialog } from "../EditCompanyDialog";
import { ApiError, ApiValidationError } from "@/lib/api/types";
import type { CompanyListItem } from "@/lib/api/types";

const company: CompanyListItem = {
  id: "00000000-0000-0000-0000-000000000009",
  nit: "900123456-1",
  razonSocial: "Renting Andino S.A.S.",
  code: "RENTANDINO",
  tenantType: "RENTING",
  estadoActivo: true,
  fechaCreacion: "2026-06-19T00:00:00Z",
  rowVersion: 4,
};

describe("EditCompanyDialog (#10118)", () => {
  it("prellena los campos y deja el código de solo lectura", () => {
    render(
      <EditCompanyDialog open company={company} onClose={vi.fn()} onUpdate={vi.fn()} onUpdated={vi.fn()} />,
    );

    expect(screen.getByLabelText(/razón social/i)).toHaveValue("Renting Andino S.A.S.");
    expect(screen.getByLabelText(/^nit$/i)).toHaveValue("900123456-1");
    const code = screen.getByLabelText(/código/i);
    expect(code).toHaveValue("RENTANDINO");
    expect(code).toBeDisabled();
  });

  it("valida requeridos en cliente y no llama a la API", async () => {
    const user = userEvent.setup();
    const onUpdate = vi.fn();
    render(
      <EditCompanyDialog open company={company} onClose={vi.fn()} onUpdate={onUpdate} onUpdated={vi.fn()} />,
    );

    await user.clear(screen.getByLabelText(/razón social/i));
    await user.clear(screen.getByLabelText(/^nit$/i));
    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByText(/la razón social es obligatoria/i)).toBeInTheDocument();
    expect(screen.getByText(/el nit es obligatorio/i)).toBeInTheDocument();
    expect(onUpdate).not.toHaveBeenCalled();
  });

  it("envía el payload editado (sin code) y notifica onUpdated en éxito", async () => {
    const user = userEvent.setup();
    const updated = { ...company, razonSocial: "Renting Andino S.A.S. (editada)", tenantType: "CONCESIONARIO" as const };
    const onUpdate = vi.fn().mockResolvedValue(updated);
    const onUpdated = vi.fn();

    render(
      <EditCompanyDialog open company={company} onClose={vi.fn()} onUpdate={onUpdate} onUpdated={onUpdated} />,
    );

    const razon = screen.getByLabelText(/razón social/i);
    await user.clear(razon);
    await user.type(razon, "Renting Andino S.A.S. (editada)");
    await user.selectOptions(screen.getByLabelText(/tipo de compañía/i), "CONCESIONARIO");
    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(onUpdate).toHaveBeenCalledWith(company.id, {
        razonSocial: "Renting Andino S.A.S. (editada)",
        nit: "900123456-1",
        tenantType: "CONCESIONARIO",
        estadoActivo: true,
        rowVersion: 4,
      }),
    );
    expect(onUpdated).toHaveBeenCalledWith(updated);
  });

  it("preserva un tipo heredado fuera del catálogo B2B (no lo fuerza a Renting)", async () => {
    // El listado incluye tenants con tipos heredados (p.ej. 'standard'); editar solo la
    // razón social debe conservar ese tipo, no mostrar 'Renting' por defecto ni enviarlo.
    const user = userEvent.setup();
    const legacy: CompanyListItem = { ...company, tenantType: "standard", code: "DEMO" };
    const onUpdate = vi.fn().mockResolvedValue(legacy);

    render(
      <EditCompanyDialog open company={legacy} onClose={vi.fn()} onUpdate={onUpdate} onUpdated={vi.fn()} />,
    );

    // El select preselecciona el tipo heredado con su etiqueta legible, no 'Renting'.
    const select = screen.getByLabelText(/tipo de compañía/i) as HTMLSelectElement;
    expect(select.value).toBe("standard");

    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(onUpdate).toHaveBeenCalledWith(legacy.id, {
        razonSocial: legacy.razonSocial,
        nit: legacy.nit,
        tenantType: "standard",
        estadoActivo: true,
        rowVersion: 4,
      }),
    );
  });

  it("mapea el error 422 del backend al campo y no cierra el modal", async () => {
    const user = userEvent.setup();
    const onUpdated = vi.fn();
    const onUpdate = vi
      .fn()
      .mockRejectedValue(
        new ApiValidationError([{ field: "nit", message: "El NIT no puede superar 20 caracteres." }], 422),
      );

    render(
      <EditCompanyDialog open company={company} onClose={vi.fn()} onUpdate={onUpdate} onUpdated={onUpdated} />,
    );

    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    expect(await screen.findByText(/el nit no puede superar 20 caracteres/i)).toBeInTheDocument();
    expect(onUpdated).not.toHaveBeenCalled();
  });

  it("envía rowVersion y muestra el aviso de edición concurrente ante un 409", async () => {
    const user = userEvent.setup();
    const onUpdated = vi.fn();
    const onUpdate = vi.fn().mockRejectedValue(new ApiError(409, "Conflict"));

    render(
      <EditCompanyDialog open company={company} onClose={vi.fn()} onUpdate={onUpdate} onUpdated={onUpdated} />,
    );

    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    // Reenvía la versión que leyó al abrir.
    expect(onUpdate).toHaveBeenCalledWith(company.id, expect.objectContaining({ rowVersion: 4 }));
    // Muestra el aviso de concurrencia (role alert) y no notifica éxito.
    expect(await screen.findByRole("alert")).toHaveTextContent(/modificada por otra persona/i);
    expect(onUpdated).not.toHaveBeenCalled();
  });

  it("cierra el modal al presionar Escape", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <EditCompanyDialog open company={company} onClose={onClose} onUpdate={vi.fn()} onUpdated={vi.fn()} />,
    );

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
