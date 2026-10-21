// HU #13666 (Feature #13636 F-F) — par Ver / Crear por tipo de servicio en los roles propios de la compañía
// (Usuarios › «Roles y permisos»). Mismo comportamiento que el editor de roles del Panel SA: el par aparece por tipo de
// servicio, «Crear» está deshabilitado sin «Ver», quitar «Ver» quita «Crear», el payload nunca lleva «Crear» sin «Ver»
// y el 400 FAMILY_CREATE_REQUIRES_READ se muestra con un mensaje claro. La API es un doble.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyRoleFormModal } from "../CompanyRoleFormModal";
import { ApiError } from "@/lib/api/types";
import {
  createTenantRole,
  getGrantablePermissions,
  getTenantRole,
  setTenantRolePermissions,
  updateTenantRole,
  type TenantRole,
  type TenantRoleDetail,
} from "@/lib/api/security";

vi.mock("@/lib/api/security", () => ({
  getGrantablePermissions: vi.fn(),
  getTenantRole: vi.fn(),
  createTenantRole: vi.fn(),
  updateTenantRole: vi.fn(),
  setTenantRolePermissions: vi.fn(),
}));

const perm = (id: string, slug: string, name: string, moduleCode: string) => ({
  id,
  slug,
  name,
  moduleCode,
  productCode: "tramites",
});

const permisos = [
  perm("mr", "matriculas.read", "Ver trámites de Matrículas", "tramites-matriculas"),
  perm("mc", "matriculas.create", "Crear trámites de Matrículas", "tramites-matriculas"),
  perm("tr", "traspasos.read", "Ver trámites de Traspasos", "tramites-traspasos"),
  perm("tc", "traspasos.create", "Crear trámites de Traspasos", "tramites-traspasos"),
  perm("or", "otros-tramites.read", "Ver otros trámites", "tramites-otros"),
  perm("oc", "otros-tramites.create", "Crear otros trámites", "tramites-otros"),
  perm("rp", "reportes.read", "Ver reportes", "reportes"),
];

const own: TenantRole = {
  id: "o1",
  code: "contador",
  name: "Contador",
  description: null,
  isSystem: false,
  permissionCount: 4,
  createdAt: "2026-02-01T00:00:00Z",
  tenantId: "tenant-1",
  productCode: "tramites",
};

const detail: TenantRoleDetail = {
  id: "o1",
  code: "contador",
  name: "Contador",
  description: null,
  isSystem: false,
  isActive: true,
  productCode: "tramites",
  tenantId: "tenant-1",
  permissions: [
    { id: "mr", slug: "matriculas.read", name: "Ver trámites de Matrículas" },
    { id: "mc", slug: "matriculas.create", name: "Crear trámites de Matrículas" },
    { id: "tr", slug: "traspasos.read", name: "Ver trámites de Traspasos" },
    { id: "rp", slug: "reportes.read", name: "Ver reportes" },
  ],
};

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getGrantablePermissions).mockResolvedValue(permisos);
  vi.mocked(getTenantRole).mockResolvedValue(detail);
  vi.mocked(createTenantRole).mockResolvedValue({ id: "o2" });
});

async function openCreate() {
  const ue = userEvent.setup();
  const onSaved = vi.fn();
  render(<CompanyRoleFormModal mode={{ kind: "create" }} onClose={vi.fn()} onSaved={onSaved} />);
  const dialog = await screen.findByRole("dialog");
  await within(dialog).findByLabelText(/^código/i);
  return { ue, dialog, onSaved };
}

describe("CompanyRoleFormModal — par Ver / Crear por tipo de servicio (HU #13666)", () => {
  it("muestra el par «Ver y crear» de Matrículas, Traspasos y Otros trámites con los nombres del Panel SA", async () => {
    const { dialog } = await openCreate();
    for (const name of ["Trámites: Matrículas", "Trámites: Traspasos", "Trámites: Otros trámites"]) {
      const group = within(dialog).getByRole("group", { name: `Ver y crear — ${name}` });
      expect(within(group).getByRole("checkbox", { name: `Ver — ${name}` })).toBeInTheDocument();
      expect(within(group).getByRole("checkbox", { name: `Crear — ${name}` })).toBeInTheDocument();
    }
    // Los módulos que no son de familia siguen con su lista normal.
    expect(within(dialog).getByRole("checkbox", { name: /ver reportes/i })).toBeInTheDocument();
  });

  it("«Crear» está deshabilitado mientras «Ver» esté apagado y se habilita al marcar «Ver»", async () => {
    const { ue, dialog } = await openCreate();
    const crear = within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Matrículas" });
    expect(crear).toBeDisabled();

    await ue.click(within(dialog).getByRole("checkbox", { name: "Ver — Trámites: Matrículas" }));
    expect(crear).toBeEnabled();
    expect(crear).not.toBeChecked();
  });

  it("quitar «Ver» quita también «Crear» y lo deja deshabilitado", async () => {
    const { ue, dialog } = await openCreate();
    const ver = within(dialog).getByRole("checkbox", { name: "Ver — Trámites: Traspasos" });
    const crear = within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Traspasos" });
    await ue.click(ver);
    await ue.click(crear);
    expect(crear).toBeChecked();

    await ue.click(ver);
    expect(ver).not.toBeChecked();
    expect(crear).not.toBeChecked();
    expect(crear).toBeDisabled();
  });

  it("el payload al crear no lleva «Crear» sin su «Ver»", async () => {
    const { ue, dialog, onSaved } = await openCreate();
    await ue.type(within(dialog).getByLabelText(/^código/i), "auditor_ext");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "Auditor externo");
    // Marca Ver + Crear de Otros trámites y luego quita Ver; marca Ver + Crear de Matrículas.
    await ue.click(within(dialog).getByRole("checkbox", { name: "Ver — Trámites: Otros trámites" }));
    await ue.click(within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Otros trámites" }));
    await ue.click(within(dialog).getByRole("checkbox", { name: "Ver — Trámites: Otros trámites" }));
    await ue.click(within(dialog).getByRole("checkbox", { name: "Ver — Trámites: Matrículas" }));
    await ue.click(within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Matrículas" }));
    await ue.click(within(dialog).getByRole("button", { name: /crear rol/i }));

    await waitFor(() => expect(createTenantRole).toHaveBeenCalledTimes(1));
    const ids = vi.mocked(createTenantRole).mock.calls[0][0].permissionIds;
    expect([...ids].sort()).toEqual(["mc", "mr"]);
    expect(onSaved).toHaveBeenCalled();
  });

  it("desmarcar todo el módulo apaga el par y marcarlo enciende Ver y Crear juntos", async () => {
    const { ue, dialog } = await openCreate();
    const todos = within(dialog).getByRole("checkbox", { name: "Seleccionar todos los permisos de Trámites: Matrículas" });
    await ue.click(todos);
    expect(within(dialog).getByRole("checkbox", { name: "Ver — Trámites: Matrículas" })).toBeChecked();
    expect(within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Matrículas" })).toBeChecked();
    await ue.click(todos);
    expect(within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Matrículas" })).not.toBeChecked();
  });

  it("al editar, quitar «Ver» de un tipo de servicio envía el rol sin su «Crear»", async () => {
    vi.mocked(updateTenantRole).mockResolvedValue(undefined as never);
    vi.mocked(setTenantRolePermissions).mockResolvedValue(undefined as never);
    const ue = userEvent.setup();
    render(<CompanyRoleFormModal mode={{ kind: "edit", role: own }} onClose={vi.fn()} onSaved={vi.fn()} />);
    const dialog = await screen.findByRole("dialog");
    const ver = await within(dialog).findByRole("checkbox", { name: "Ver — Trámites: Matrículas" });
    expect(within(dialog).getByRole("checkbox", { name: "Crear — Trámites: Matrículas" })).toBeChecked();

    await ue.click(ver);
    await ue.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => expect(setTenantRolePermissions).toHaveBeenCalledTimes(1));
    const [, ids] = vi.mocked(setTenantRolePermissions).mock.calls[0];
    expect([...ids].sort()).toEqual(["rp", "tr"]);
  });

  it("un 400 FAMILY_CREATE_REQUIRES_READ se muestra con un mensaje claro en español, sin códigos", async () => {
    vi.mocked(createTenantRole).mockRejectedValue(
      new ApiError(400, "bad request", { code: "FAMILY_CREATE_REQUIRES_READ", permissions: ["traspasos.create"] }),
    );
    const { ue, dialog } = await openCreate();
    await ue.type(within(dialog).getByLabelText(/^código/i), "auditor_ext");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "Auditor externo");
    await ue.click(within(dialog).getByRole("button", { name: /crear rol/i }));

    const alert = await within(dialog).findByRole("alert");
    expect(alert).toHaveTextContent(
      "Para habilitar «Crear» primero debe estar habilitado «Ver» en ese tipo de servicio.",
    );
    expect(alert).not.toHaveTextContent(/traspasos\.create|FAMILY_CREATE_REQUIRES_READ/);
  });
});
