// HU #14695 · M19 (plan §3.1 y §9; contrato platform.v1.yaml 1.1.0 `TenantProductProvisioning`; spec-visual §3.11).
// Uso de ejemplo:
//   <EstadoAprovisionamiento tenantId="t1" provisioning={{ status: "ERROR", action: "REINTENTAR", … }}
//     impuestosUrl="https://impuestos.test" onEditarCompania={abrir} onReintentado={releer} />
// Pinta «Conexión con el proveedor:» + chip con la etiqueta del contrato + el motivo que trae el servidor + la acción.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { EstadoAprovisionamiento } from "../tabs/EstadoAprovisionamiento";
import type { ProvisioningAction, ProvisioningStatus, TenantProductProvisioning } from "@/lib/api/platform";

const retryTenantProductProvisioning = vi.fn();
vi.mock("@/lib/api/platform", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/platform")>()),
  retryTenantProductProvisioning: (...a: unknown[]) => retryTenantProductProvisioning(...a),
}));

const prov = (status: ProvisioningStatus, action: ProvisioningAction | null, extra: Partial<TenantProductProvisioning> = {}): TenantProductProvisioning => ({
  status,
  reason: null,
  message: null,
  action,
  signatureConfirmed: null,
  updatedAt: "2026-11-24T15:00:00Z",
  ...extra,
});

const grupo = () => screen.getByRole("group", { name: "Conexión con el proveedor de Impuestos" });

describe("EstadoAprovisionamiento", () => {
  beforeEach(() => {
    retryTenantProductProvisioning.mockReset().mockResolvedValue(undefined);
  });

  // Los 8 estados del plan §3.1 con la etiqueta (x-flit-etiquetas) y la acción que manda el contrato.
  const CASOS: [ProvisioningStatus, ProvisioningAction | null, string, string | null][] = [
    ["PENDIENTE", null, "Pendiente", null],
    ["LISTO", null, "Listo", null],
    ["LISTO_SIN_WEBHOOK", "CARGAR_SECRETO", "Listo sin webhook", "Cargar secreto"],
    ["ERROR", "REINTENTAR", "Error", "Reintentar"],
    ["INCIERTO", "REINTENTAR", "Incierto", "Reintentar"],
    ["REQUIERE_CREDENCIALES", "CARGAR_CREDENCIALES", "Requiere credenciales", "Cargar credenciales"],
    ["REQUIERE_REVISION_NIT", "CORREGIR_NIT", "Requiere revisión del NIT", "Corregir NIT"],
    ["SUSPENDIDO", null, "Suspendido", null],
  ];

  it.each(CASOS)("%s: chip «%s» y su acción", (status, action, etiqueta, accion) => {
    render(
      <EstadoAprovisionamiento
        tenantId="t1"
        provisioning={prov(status, action, { message: `Motivo de ${status}` })}
        impuestosUrl="https://impuestos.test"
        onEditarCompania={() => {}}
        onReintentado={() => {}}
      />,
    );
    const g = grupo();
    expect(within(g).getByText("Conexión con el proveedor:")).toBeInTheDocument();
    expect(within(g).getByText(etiqueta)).toBeInTheDocument();
    expect(within(g).getByLabelText(`Conexión con el proveedor: ${etiqueta}`)).toBeInTheDocument();
    expect(within(g).getByText(`Motivo de ${status}`)).toBeInTheDocument();
    const acciones = [...within(g).queryAllByRole("button"), ...within(g).queryAllByRole("link")];
    if (accion === null) {
      expect(acciones).toHaveLength(0);
    } else {
      expect(acciones).toHaveLength(1);
      expect(acciones[0]).toHaveTextContent(accion);
    }
  });

  it("LISTO con la firma sin confirmar lo marca con la etiqueta del contrato", () => {
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("LISTO", null, { signatureConfirmed: false })} impuestosUrl={null} onReintentado={() => {}} />);
    expect(within(grupo()).getByText("Firma sin confirmar")).toBeInTheDocument();
  });

  it("LISTO con la firma confirmada no muestra la marca", () => {
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("LISTO", null, { signatureConfirmed: true })} impuestosUrl={null} onReintentado={() => {}} />);
    expect(within(grupo()).queryByText("Firma sin confirmar")).toBeNull();
  });

  it("sin fila todavía: chip neutro «Sin estado todavía» y el aviso", () => {
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={null} impuestosUrl={null} onReintentado={() => {}} />);
    const g = grupo();
    expect(within(g).getByText("Sin estado todavía")).toBeInTheDocument();
    expect(within(g).getByText("Aparece en unos segundos tras encender Impuestos.")).toBeInTheDocument();
    expect(within(g).queryByRole("button")).toBeNull();
  });

  it("un estado desconocido dice «Sin clasificar», nunca el código crudo", () => {
    render(
      <EstadoAprovisionamiento
        tenantId="t1"
        provisioning={prov("OTRO_ESTADO" as ProvisioningStatus, null)}
        impuestosUrl={null}
        onReintentado={() => {}}
      />,
    );
    expect(within(grupo()).getByText("Sin clasificar")).toBeInTheDocument();
    expect(within(grupo()).queryByText("OTRO_ESTADO")).toBeNull();
  });

  it("Reintentar llama al POST y avisa para releer", async () => {
    const user = userEvent.setup();
    const onReintentado = vi.fn();
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("ERROR", "REINTENTAR")} impuestosUrl={null} onReintentado={onReintentado} />);
    await user.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(retryTenantProductProvisioning).toHaveBeenCalledWith("t1", "impuestos");
    expect(onReintentado).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("si el reintento falla, lo dice con role=alert", async () => {
    const user = userEvent.setup();
    retryTenantProductProvisioning.mockRejectedValue(new Error("boom"));
    const onReintentado = vi.fn();
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("INCIERTO", "REINTENTAR")} impuestosUrl={null} onReintentado={onReintentado} />);
    await user.click(screen.getByRole("button", { name: "Reintentar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("No se pudo pedir el reintento. Intenta de nuevo.");
    expect(onReintentado).not.toHaveBeenCalled();
  });

  it("con la URL de Impuestos (me/apps), credenciales y secreto son enlaces al backoffice en otra pestaña", () => {
    const { rerender } = render(
      <EstadoAprovisionamiento tenantId="t 1" provisioning={prov("REQUIERE_CREDENCIALES", "CARGAR_CREDENCIALES")} impuestosUrl="https://impuestos.test/" onReintentado={() => {}} />,
    );
    const enlace = screen.getByRole("link", { name: /Cargar credenciales/ });
    expect(enlace).toHaveAttribute("href", "https://impuestos.test/backoffice/empresas/t%201");
    expect(enlace).toHaveAttribute("target", "_blank");
    expect(enlace).toHaveAttribute("rel", "noopener");
    expect(within(enlace).getByText("(se abre en otra pestaña)")).toHaveClass("sr-only");
    expect(enlace).toHaveAccessibleName("Cargar credenciales (se abre en otra pestaña)");

    rerender(
      <EstadoAprovisionamiento tenantId="t1" provisioning={prov("LISTO_SIN_WEBHOOK", "CARGAR_SECRETO")} impuestosUrl="https://impuestos.test" onReintentado={() => {}} />,
    );
    expect(screen.getByRole("link", { name: /Cargar secreto/ })).toHaveAttribute("href", "https://impuestos.test/backoffice/empresas/t1");
  });

  it("sin la URL de Impuestos no pinta el enlace: indica hacerlo desde el backoffice", () => {
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("REQUIERE_CREDENCIALES", "CARGAR_CREDENCIALES")} impuestosUrl={null} onReintentado={() => {}} />);
    expect(screen.queryByRole("link")).toBeNull();
    expect(within(grupo()).getByText("Cargar credenciales: hazlo desde Impuestos › Backoffice")).toBeInTheDocument();
  });

  it("sin la URL de Impuestos, el secreto conserva el nombre de la acción en el texto de respaldo", () => {
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("LISTO_SIN_WEBHOOK", "CARGAR_SECRETO")} impuestosUrl={null} onReintentado={() => {}} />);
    expect(screen.queryByRole("link")).toBeNull();
    expect(within(grupo()).getByText("Cargar secreto: hazlo desde Impuestos › Backoffice")).toBeInTheDocument();
  });

  it("Corregir NIT: con onEditarCompania es un botón que lo llama", async () => {
    const user = userEvent.setup();
    const onEditarCompania = vi.fn();
    render(
      <EstadoAprovisionamiento tenantId="t1" provisioning={prov("REQUIERE_REVISION_NIT", "CORREGIR_NIT")} impuestosUrl={null} onEditarCompania={onEditarCompania} onReintentado={() => {}} />,
    );
    await user.click(screen.getByRole("button", { name: "Corregir NIT" }));
    expect(onEditarCompania).toHaveBeenCalledTimes(1);
  });

  it("Corregir NIT: sin onEditarCompania indica dónde se corrige", () => {
    render(<EstadoAprovisionamiento tenantId="t1" provisioning={prov("REQUIERE_REVISION_NIT", "CORREGIR_NIT")} impuestosUrl={null} onReintentado={() => {}} />);
    expect(screen.queryByRole("button")).toBeNull();
    expect(within(grupo()).getByText("Corrige el NIT con «Editar compañía».")).toBeInTheDocument();
  });
});
