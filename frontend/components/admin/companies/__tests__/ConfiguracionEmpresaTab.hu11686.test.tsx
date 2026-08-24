// HU #11686 — El panel de documentos personalizados (HU #11315, Feature #11309, ADR-0042) deja de
// ser visible en «Configuración Empresa» para los dos roles de esa superficie: SuperAdmin y
// AdminCompany. La API NO se cierra (los documentos ya cargados siguen aplicándose): esto es solo
// ocultamiento de UI, consecuencia aceptada por el PO humano y registrada en el ADR-0050.
//
// El caso que importa es el canal `TENANT_API`: era la ÚNICA condición bajo la que el panel llegaba
// a renderizarse (DT-7 del plan técnico). Probar con `FLIT_SMTP` no demostraría nada, porque ahí
// tampoco se renderizaba antes del cambio.
//
// Uso de ejemplo:
//   render(<CompanyConfigTabs settings={{ ...settings, enrutamientoSMTP: "TENANT_API" }} ... />);
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyConfigTabs } from "../CompanyConfigTabs";
import type { TenantSettings } from "@/lib/api/types";

const settings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: true,
    onlyOwnVehiclesByFamily: { matriculas: true, traspaso: true, otros: true },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  },
  baulFirmasActivo: true,
  preasignacionPlacaActiva: false,
  plateFlowSkipToTerminado: false,
  validarSoatConRunt: false,
  // El canal que ANTES hacía visible el panel.
  enrutamientoSMTP: "TENANT_API",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};

async function abrirConfiguracionEmpresa() {
  const user = userEvent.setup({ delay: null });
  render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn().mockResolvedValue(undefined)} />);
  const tab = screen.queryByRole("tab", { name: /configuraci[oó]n empresa/i });
  if (tab) await user.click(tab);
  return user;
}

describe("ConfiguracionEmpresaTab — HU #11686", () => {
  it("AC1: no renderiza el panel de documentos personalizados aunque el canal sea TENANT_API", async () => {
    await abrirConfiguracionEmpresa();

    expect(
      screen.queryByRole("region", { name: /documentos personalizados de la compañía/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: /^documentos personalizados$/i }),
    ).not.toBeInTheDocument();
  });

  it("AC2: no queda ningún control de carga o activación de documentos personalizados", async () => {
    await abrirConfiguracionEmpresa();

    expect(screen.queryByTestId("personalized-doc-empty-mandato")).not.toBeInTheDocument();
    expect(screen.queryByTestId("personalized-doc-empty-tramite_virtual")).not.toBeInTheDocument();
    // Ojo con aseverar sobre «API Renting cliente»: ese texto tambien esta en el selector de canal
    // y en su descripcion, que siguen ahi a proposito. Lo que debe desaparecer es el panel.
    expect(screen.queryByRole("button", { name: /cargar documento/i })).not.toBeInTheDocument();
  });

  it("AC3: el resto de la pestaña sigue en pie — el selector de canal no se toca", async () => {
    await abrirConfiguracionEmpresa();

    // Guardarraíl de alcance: ocultar el panel no puede llevarse por delante la configuración
    // de canal, que es de otra HU y sigue siendo editable.
    expect(screen.getByLabelText(/avisos de correo al cambio de estado/i)).toBeInTheDocument();
  });
});
