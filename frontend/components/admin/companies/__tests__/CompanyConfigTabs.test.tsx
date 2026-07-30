// AC2 — Formulario multi-pestaña con guardado atómico. "Guardar todo" detecta los cambios y
// abre una ventana de confirmación; al confirmar dispara un único PUT con todos los campos.
// Un 422 cierra la confirmación y marca los campos inválidos.
//
// Uso de ejemplo:
//   render(<CompanyConfigTabs settings={settings} onSaveSettings={spy} />);
import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyConfigTabs } from "../CompanyConfigTabs";
import { ApiValidationError, type TenantSettings } from "@/lib/api/types";

const settings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: true,
  },
  baulFirmasActivo: true,
  preasignacionPlacaActiva: false,
  plateFlowSkipToTerminado: false,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};

describe("CompanyConfigTabs (AC2)", () => {
  it("confirma y dispara un único PUT con todos los campos", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    // "Guardar todo" no guarda directo: abre la confirmación.
    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText(/no se detectaron cambios/i)).toBeInTheDocument();
    expect(onSaveSettings).not.toHaveBeenCalled();

    // Al confirmar sí persiste (un único PUT con todo el payload).
    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    expect(onSaveSettings).toHaveBeenCalledTimes(1);
    // Este objeto afirma el contrato COMPLETO que emite `formToUpdate` (settingsForm.ts): es una
    // aserción exacta, no un `objectContaining`. Al añadir un campo nuevo al PUT de configuración
    // hay que reflejarlo aquí, o este test queda obsoleto y falla con el campo ausente.
    expect(onSaveSettings).toHaveBeenCalledWith({
      switchesMatricula: {
        allowInitialRegistration: true,
        allowMiscNewVehicles: false,
        onlyOwnVehicles: true,
      },
      baulFirmasActivo: true,
      preasignacionPlacaActiva: false,
      plateFlowSkipToTerminado: false,
      enrutamientoSMTP: "FLIT_SMTP",
      notificationTarget: "COMPRADOR",
      metodosRecaudo: ["Pasarela FLIT"],
      // HU #10478 — defaults Kyverum-first cuando la config no viene en settings.
      runtFailoverTimeoutMs: 60000,
      consultationProviderConfig: {
        vehicle_vin: { primary: "kyverum_runt", fallback: ["verifik"] },
        vehicle_plate: { primary: "kyverum_runt", fallback: ["verifik"] },
        conductor: { primary: "kyverum_runt_conductor", fallback: ["verifik_conductor"] },
      },
      // Feature #10707 — default sin config: solo Fasecolda, sugerido Fasecolda.
      avaluoProviderConfig: {
        primary: "fasecolda",
        enabled: ["fasecolda"],
      },
      // FEATURE 02 (HU #10723/#10724) — default sin config: 'external' (SIMIT en línea).
      finesQuerySource: "external",
    });

    // El resultado se muestra en la misma ventana (fase éxito), no como banner fijo.
    expect(await screen.findByText(/cambios guardados/i)).toBeInTheDocument();
  });

  it("detecta el cambio real (activar/desactivar) por módulo y lo incluye en el PUT", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    // Pestaña Matrícula Inicial activa por defecto. Estado inicial: inicial=ON, misceláneas=OFF.
    await user.click(screen.getByLabelText(/permitir matrícula inicial/i)); // ON → OFF (Desactivar)
    await user.click(screen.getByLabelText(/permitir vehículos de categorías misceláneas/i)); // OFF → ON (Activar)
    await user.click(screen.getByRole("button", { name: /guardar todo/i }));

    // La confirmación agrupa por módulo y describe el cambio REAL de cada campo.
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByText("Matrícula Inicial")).toBeInTheDocument();
    expect(within(dialog).getByText(/permitir matrícula inicial/i)).toBeInTheDocument();
    expect(within(dialog).getByText("Desactivar")).toBeInTheDocument();
    expect(within(dialog).getByText("Activar")).toBeInTheDocument();

    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    expect(onSaveSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        switchesMatricula: expect.objectContaining({
          allowInitialRegistration: false,
          allowMiscNewVehicles: true,
        }),
      }),
    );
  });

  it("HU #10478: cambia el proveedor de consulta por VIN y lo incluye en el PUT con su fallback", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    // La sección de proveedores vive en la pestaña Configuración Empresa.
    await user.click(screen.getByRole("tab", { name: /configuración empresa/i }));
    await user.selectOptions(screen.getByLabelText("Vehículo por VIN"), "verifik");

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    const dialog = screen.getByRole("dialog");
    // El resumen agrupa el cambio bajo el módulo de proveedores.
    expect(within(dialog).getByText(/proveedores de consulta runt/i)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: /guardar cambios/i }));

    expect(onSaveSettings).toHaveBeenCalledWith(
      expect.objectContaining({
        consultationProviderConfig: expect.objectContaining({
          vehicle_vin: { primary: "verifik", fallback: ["kyverum_runt"] },
          vehicle_plate: { primary: "kyverum_runt", fallback: ["verifik"] },
          conductor: { primary: "kyverum_runt_conductor", fallback: ["verifik_conductor"] },
        }),
      }),
    );
  });

  it("HU #10929: la pestaña Representantes agrupa el contenido y ya no hay pestañas Escrituras ni Baúl", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(
      <CompanyConfigTabs
        settings={settings}
        onSaveSettings={onSaveSettings}
        legalRepresentativesSlot={<div>slot representantes</div>}
      />,
    );

    // Existe la pestaña de representantes; ya NO existen pestañas propias de Escrituras ni Baúl.
    const repTab = screen.getByRole("tab", { name: /representantes legales/i });
    expect(screen.queryByRole("tab", { name: /escrituras/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /baúl de firmas/i })).not.toBeInTheDocument();

    await user.click(repTab);
    expect(screen.getByText("slot representantes")).toBeInTheDocument();
  });

  it("cancela la confirmación sin guardar", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi.fn().mockResolvedValue(undefined);

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /cancelar/i }));

    expect(onSaveSettings).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("ante un 422 cierra la confirmación y marca los campos inválidos", async () => {
    const user = userEvent.setup();
    const onSaveSettings = vi
      .fn()
      .mockRejectedValue(
        new ApiValidationError(
          [{ field: "enrutamientoSMTP", message: "Valor inválido para el canal." }],
          422,
        ),
      );

    render(<CompanyConfigTabs settings={settings} onSaveSettings={onSaveSettings} />);

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /guardar cambios/i }));

    // 422 → se cierra la confirmación y aparece el aviso de campos inválidos.
    expect(await screen.findByText(/revisa los campos marcados/i)).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    // El detalle del campo se muestra en la pestaña Configuración Empresa.
    await user.click(screen.getByRole("tab", { name: /configuración empresa/i }));
    await waitFor(() =>
      expect(screen.getByText(/valor inválido para el canal/i)).toBeInTheDocument(),
    );
  });
});

// HU #11062 — el tenant solo vivía en la URL: nada en pantalla confirmaba sobre qué compañía se
// guardaba, mientras "Guardar todo" persiste con un único PUT atómico.
describe("CompanyConfigTabs — identificación de la compañía (HU #11062)", () => {
  const company = { razonSocial: "Transportes ACME SAS", nit: "900123456-7" };

  it("rotula la compañía y el encabezado sobrevive al cambio de pestaña", async () => {
    const user = userEvent.setup();
    render(
      <CompanyConfigTabs settings={settings} company={company} onSaveSettings={vi.fn()} />,
    );

    const header = screen.getByLabelText("Compañía en configuración");
    expect(within(header).getByText("Transportes ACME SAS")).toBeInTheDocument();
    expect(within(header).getByText(/900123456-7/)).toBeInTheDocument();

    // El encabezado va POR ENCIMA de la barra de pestañas: cambiar de pestaña no lo quita.
    await user.click(screen.getByRole("tab", { name: /historial de cambios/i }));
    expect(screen.getByLabelText("Compañía en configuración")).toBeInTheDocument();
  });

  it("identifica la compañía en la confirmación de guardado", async () => {
    const user = userEvent.setup();
    render(
      <CompanyConfigTabs settings={settings} company={company} onSaveSettings={vi.fn()} />,
    );

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));

    const callout = within(screen.getByRole("dialog")).getByTestId("save-config-company");
    expect(within(callout).getByText("Transportes ACME SAS")).toBeInTheDocument();
    expect(within(callout).getByText(/900123456-7/)).toBeInTheDocument();
  });

  it("sin identidad resuelta la pantalla funciona igual, sin hueco", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} company={null} onSaveSettings={vi.fn()} />);

    expect(screen.queryByLabelText("Compañía en configuración")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    expect(
      within(screen.getByRole("dialog")).queryByTestId("save-config-company"),
    ).not.toBeInTheDocument();
  });
});
