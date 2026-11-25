// HU #14695 · M19 (plan §1.3 D-TAXID/D-EMAIL; contrato platform.v1.yaml 1.1.0 `ProvisioningPreview`; spec-visual §3.11).
// Uso de ejemplo:
//   <DialogoActivacionImpuestos open tenantId="t1" confirmando={false} error={null}
//     onCancel={cerrar} onConfirm={() => setTenantProduct(id, "impuestos", true)} />
// Lee GET …/products/impuestos/alta-previa y muestra el NIT base y el correo que se enviarán. SIN_ADMIN bloquea.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DialogoActivacionImpuestos } from "../tabs/DialogoActivacionImpuestos";
import type { ProvisioningPreview } from "@/lib/api/platform";

const getTenantProductProvisioningPreview = vi.fn();
vi.mock("@/lib/api/platform", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/platform")>()),
  getTenantProductProvisioningPreview: (...a: unknown[]) => getTenantProductProvisioningPreview(...a),
}));

const preview = (extra: Partial<ProvisioningPreview> = {}): ProvisioningPreview => ({
  legalName: "Compañía Demo S.A.S.",
  taxIdBase: "900123456",
  nitStatus: "VALIDO",
  contactEmail: "nombre@empresa.com",
  blocking: null,
  ...extra,
});

const dialogo = () => screen.getByRole("dialog", { name: "Activar Impuestos" });
const cta = () => within(dialogo()).getByRole("button", { name: "Activar Impuestos" });

function montar(props: Partial<React.ComponentProps<typeof DialogoActivacionImpuestos>> = {}) {
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  render(
    <DialogoActivacionImpuestos open tenantId="t1" confirmando={false} error={null} onConfirm={onConfirm} onCancel={onCancel} {...props} />,
  );
  return { onConfirm, onCancel };
}

describe("DialogoActivacionImpuestos", () => {
  beforeEach(() => {
    getTenantProductProvisioningPreview.mockReset();
  });

  it("muestra el NIT base (tax_id), la razón social y el correo del Administrador de Compañía", async () => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview());
    const { onConfirm } = montar();

    expect(await within(dialogo()).findByText("900123456")).toHaveClass("tabular-nums");
    expect(getTenantProductProvisioningPreview).toHaveBeenCalledWith("t1", "impuestos");
    const d = dialogo();
    expect(within(d).getByText("Antes de encenderlo, revisa los datos con los que se creará la compañía en el proveedor.")).toBeInTheDocument();
    expect(within(d).getByText("NIT que se enviará")).toBeInTheDocument();
    expect(within(d).getByText("Razón social")).toBeInTheDocument();
    expect(within(d).getByText("Compañía Demo S.A.S.")).toBeInTheDocument();
    expect(within(d).getByText("Correo de contacto")).toBeInTheDocument();
    expect(within(d).getByText("nombre@empresa.com")).toBeInTheDocument();
    expect(within(d).getByText("Administrador de Compañía más antiguo")).toBeInTheDocument();
    expect(within(d).queryByRole("alert")).toBeNull();
    expect(cta()).toBeEnabled();

    await userEvent.setup().click(cta());
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it("SIN_ADMIN: aviso del contrato y el CTA deshabilitado con ese motivo asociado", async () => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview({ contactEmail: null, blocking: "SIN_ADMIN" }));
    const { onConfirm } = montar();

    const aviso = await within(dialogo()).findByText("La compañía no tiene un Administrador de Compañía activo");
    expect(cta()).toBeDisabled();
    const motivoId = aviso.closest("[id]")?.id;
    expect(motivoId).toBeTruthy();
    expect(cta().getAttribute("aria-describedby")).toContain(motivoId);
    expect(document.getElementById(motivoId!)).not.toBeNull();
    // Correo ausente: «Sin dato», nunca vacío.
    expect(within(dialogo()).getByText("Sin dato")).toBeInTheDocument();
    await userEvent.setup().click(cta());
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it.each([
    ["AMBIGUO", "No se puede separar el dígito de verificación del NIT"],
    ["AUSENTE", "La compañía no tiene NIT"],
    ["IGUAL_A_CABEZA", "El NIT es el mismo de su compañía cabeza"],
  ] as const)("NIT %s: advierte con el motivo y deja activar", async (nitStatus, motivo) => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview({ nitStatus, taxIdBase: null }));
    montar();
    expect(
      await within(dialogo()).findByText(
        `El NIT de la compañía requiere revisión: ${motivo}. Impuestos quedará en «Requiere revisión del NIT» hasta que lo corrijas en «Editar compañía».`,
      ),
    ).toBeInTheDocument();
    expect(cta()).toBeEnabled();
  });

  it("razón social de más de 200 caracteres: advierte que el proveedor la rechazará", async () => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview({ legalName: "A".repeat(201) }));
    montar();
    expect(
      await within(dialogo()).findByText("La razón social pasa de 200 caracteres: el proveedor la rechazará. Acórtala en «Editar compañía»."),
    ).toBeInTheDocument();
    expect(cta()).toBeEnabled();
  });

  it("razón social de 200 caracteres exactos no advierte", async () => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview({ legalName: "A".repeat(200) }));
    montar();
    await within(dialogo()).findByText("900123456");
    expect(within(dialogo()).queryByText(/pasa de 200 caracteres/)).toBeNull();
  });

  it("cargando: dice que consulta los datos y el CTA está deshabilitado", () => {
    getTenantProductProvisioningPreview.mockReturnValue(new Promise(() => {}));
    montar();
    expect(within(dialogo()).getByText("Consultando los datos del alta…")).toBeInTheDocument();
    expect(cta()).toBeDisabled();
  });

  it("error al leer: lo dice y «Reintentar» vuelve a consultar", async () => {
    const user = userEvent.setup();
    getTenantProductProvisioningPreview.mockRejectedValueOnce(new Error("x")).mockResolvedValueOnce(preview());
    montar();
    expect(await within(dialogo()).findByText("No se pudieron leer los datos del alta")).toBeInTheDocument();
    expect(cta()).toBeDisabled();
    await user.click(within(dialogo()).getByRole("button", { name: "Reintentar" }));
    expect(await within(dialogo()).findByText("900123456")).toBeInTheDocument();
    expect(getTenantProductProvisioningPreview).toHaveBeenCalledTimes(2);
  });

  it("el error del PUT se muestra dentro del diálogo, que sigue abierto", async () => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview());
    montar({ error: "La compañía no tiene un Administrador de Compañía activo" });
    await within(dialogo()).findByText("900123456");
    expect(within(dialogo()).getByRole("alert")).toHaveTextContent("La compañía no tiene un Administrador de Compañía activo");
  });

  it("Cancelar cierra sin confirmar", async () => {
    getTenantProductProvisioningPreview.mockResolvedValue(preview());
    const { onCancel, onConfirm } = montar();
    await within(dialogo()).findByText("900123456");
    await userEvent.setup().click(within(dialogo()).getByRole("button", { name: "Cancelar" }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("cerrado no consulta nada", () => {
    montar({ open: false });
    expect(getTenantProductProvisioningPreview).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
