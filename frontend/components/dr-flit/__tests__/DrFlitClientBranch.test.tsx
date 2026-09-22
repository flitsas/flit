import { describe, expect, it, vi } from "vitest";

import { render, screen } from "@testing-library/react";

import { DrFlitClientBranchChoices } from "../DrFlitClientBranch";

// HU #12711 — la API de Validación de Identidad exige el permiso del módulo y rechaza a los
// organismos: Dr. Flit no ofrece esa búsqueda a quien no ve el módulo en el menú.
describe("DrFlitClientBranchChoices — búsqueda de validaciones según el módulo (HU #12711)", () => {
  it("con el módulo visible ofrece trámites y validación de identidad", () => {
    render(<DrFlitClientBranchChoices onSelect={vi.fn()} canSearchValidaciones />);

    expect(screen.getByRole("button", { name: /Ver trámites/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Ver validación de identidad/ })).toBeInTheDocument();
  });

  it("sin el módulo solo ofrece trámites", () => {
    render(<DrFlitClientBranchChoices onSelect={vi.fn()} canSearchValidaciones={false} />);

    expect(screen.getByRole("button", { name: /Ver trámites/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Ver validación de identidad/ })).not.toBeInTheDocument();
  });
});
