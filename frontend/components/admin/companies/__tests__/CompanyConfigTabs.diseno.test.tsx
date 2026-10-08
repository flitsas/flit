// Rediseño de las pestañas de configuración de la compañía: modelo de guardado inequívoco, tablist con
// teclado, acordeones de Trámites con resumen, tarjetas con estado en texto y estructura de Configuración Empresa.
import { describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { CompanyConfigTabs } from "../CompanyConfigTabs";
import type { TenantSettings } from "@/lib/api/types";

vi.mock("@/lib/api/platform", () => ({ listTenantProducts: vi.fn().mockResolvedValue([]), setTenantProduct: vi.fn() }));

const settings: TenantSettings = {
  tenantId: "t1",
  switchesMatricula: {
    allowInitialRegistration: true,
    allowMiscNewVehicles: false,
    onlyOwnVehicles: false,
    onlyOwnVehiclesByFamily: { matriculas: false, traspaso: false, otros: false },
    blockProcedureFamily: { matriculas: false, traspaso: false, otros: false },
  },
  baulFirmasActivo: true,
  preasignacionPlacaActiva: false,
  validarSoatConRunt: false,
  enrutamientoSMTP: "FLIT_SMTP",
  notificationTarget: "COMPRADOR",
  metodosRecaudo: ["Pasarela FLIT"],
};

const slots = {
  documentosSlot: <div>panel-documentos</div>,
  auditSlot: <div>panel-historial</div>,
  otSlot: <div>panel-organismos</div>,
};

describe("Contenedor de pestañas — modelo de guardado", () => {
  it("muestra «Cambios sin guardar» al editar y lo quita al guardar", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn().mockResolvedValue(undefined)} />);
    await user.click(screen.getByRole("tab", { name: "Trámites" }));

    expect(screen.queryByText("Cambios sin guardar")).not.toBeInTheDocument();
    await user.click(screen.getByLabelText(/permitir vehículos de categorías misceláneas/i));
    expect(screen.getByText("Cambios sin guardar")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /guardar todo/i }));
    await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: /guardar cambios/i }));
    expect(await screen.findByText(/cambios guardados/i)).toBeInTheDocument();
    expect(screen.queryByText("Cambios sin guardar")).not.toBeInTheDocument();
  });

  it("«Guardar todo» no aparece fuera del PUT (Documentos, Historial)", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} {...slots} />);

    expect(screen.getByRole("button", { name: /guardar todo/i })).toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: "Documentos" }));
    expect(screen.queryByRole("button", { name: /guardar todo/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: /historial/i }));
    expect(screen.queryByRole("button", { name: /guardar todo/i })).not.toBeInTheDocument();
    await user.click(screen.getByRole("tab", { name: "Trámites" }));
    expect(screen.getByRole("button", { name: /guardar todo/i })).toBeInTheDocument();
  });

  it("el tablist se recorre con flechas, Inicio y Fin, con tabpanel enlazado", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} {...slots} />);

    const primera = screen.getByRole("tab", { name: "Trámites" });
    primera.focus();
    expect(primera).toHaveAttribute("tabindex", "0");
    expect(screen.getByRole("tab", { name: /configuración empresa/i })).toHaveAttribute("tabindex", "-1");

    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("tab", { name: /configuración empresa/i })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: /configuración empresa/i })).toHaveFocus();

    await user.keyboard("{End}");
    expect(screen.getByRole("tab", { name: /historial/i })).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{ArrowRight}");
    expect(screen.getByRole("tab", { name: "Trámites" })).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{ArrowLeft}");
    expect(screen.getByRole("tab", { name: /historial/i })).toHaveFocus();
    await user.keyboard("{Home}");
    expect(screen.getByRole("tab", { name: "Trámites" })).toHaveFocus();

    const panel = screen.getByRole("tabpanel");
    expect(panel).toHaveAttribute("aria-labelledby", screen.getByRole("tab", { name: "Trámites" }).id);
    expect(screen.getByRole("tab", { name: "Trámites" })).toHaveAttribute("aria-controls", panel.id);
  });
});

describe("Pestaña Trámites — acordeones y tarjetas", () => {
  it("cada familia resume sus restricciones en el encabezado y las tarjetas dicen su estado en texto", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} />);
    await user.click(screen.getByRole("tab", { name: "Trámites" }));

    const matriculas = screen.getByRole("button", { name: /matrículas/i });
    expect(matriculas).toHaveAccessibleDescription(/sin restricciones/i);
    expect(screen.getAllByText("Sin restricciones").length).toBe(3);
    // Sin doble negación: «No permitir…» apagado se lee «Permitido».
    expect(screen.getByText("Permitido")).toBeInTheDocument();
    expect(screen.getByText("No permitido")).toBeInTheDocument(); // misceláneos apagado

    await user.click(screen.getByLabelText(/no permitir trámites de matrículas/i));
    expect(screen.getByText("Bloqueado")).toBeInTheDocument();
    expect(screen.getByText("1 restricción activa")).toBeInTheDocument();
    await user.click(screen.getByLabelText(/^solo vehículos propios$/i));
    expect(screen.getByText("2 restricciones activas")).toBeInTheDocument();
    expect(screen.getByText("Restringido")).toBeInTheDocument();
  });

  it("Improntas usa el mismo acordeón, abierto por defecto, con chip de estado a la derecha", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} canConfigureImprontas />);
    await user.click(screen.getByRole("tab", { name: "Trámites" }));

    const header = screen.getByRole("button", { name: /^improntas/i });
    expect(header).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByTestId("improntas-estado")).toHaveTextContent("Habilitada");
    await user.click(header);
    expect(screen.queryByRole("switch", { name: "Generación de improntas" })).not.toBeInTheDocument();
  });

  it("la descripción larga se recorta con «Ver más» sin perder el texto", async () => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} />);
    await user.click(screen.getByRole("tab", { name: "Trámites" }));

    const verMas = screen.getAllByRole("button", { name: "Ver más" })[0]!;
    expect(verMas).toHaveAttribute("aria-expanded", "false");
    await user.click(verMas);
    expect(screen.getAllByRole("button", { name: "Ver menos" })[0]).toHaveAttribute("aria-expanded", "true");
  });
});

describe("Pestaña Configuración Empresa — estructura", () => {
  const abrir = async (extra: Record<string, unknown> = {}) => {
    const user = userEvent.setup();
    render(<CompanyConfigTabs settings={settings} onSaveSettings={vi.fn()} {...slots} {...extra} />);
    await user.click(screen.getByRole("tab", { name: /configuración empresa/i }));
    return user;
  };

  it("agrupa por intención en tarjetas con título y ofrece un índice de anclas con nombre accesible", async () => {
    await abrir();

    const titulos = ["Firma y documentos", "Módulos activos del dashboard", "Notificaciones", "Cobro y recaudo", "Consultas y fuentes"];
    for (const t of titulos) {
      expect(screen.getByRole("heading", { level: 3, name: t })).toBeInTheDocument();
    }
    const nav = screen.getByRole("navigation", { name: /bloques de configuración/i });
    expect(within(nav).getAllByRole("link")).toHaveLength(6);
    expect(within(nav).getByRole("link", { name: "Ir al bloque Cobro y recaudo" })).toHaveAttribute("href", "#cfg-recaudo");
    expect(screen.getByText("panel-organismos")).toBeInTheDocument();
  });

  it("las opciones múltiples y únicas son tarjetas seleccionables con rol checkbox/radio", async () => {
    const user = await abrir();

    const pasarela = screen.getByRole("checkbox", { name: /pasarela flit/i });
    expect(pasarela).toBeChecked();
    await user.click(pasarela);
    expect(pasarela).not.toBeChecked();

    const grupo = screen.getByRole("radiogroup", { name: /fuente de comparendos/i });
    const [interna, externa] = within(grupo).getAllByRole("radio");
    expect(externa).toBeChecked();
    interna!.focus();
    await user.keyboard(" ");
    expect(interna).toBeChecked();
  });

  it("Fasecolda aparece marcada y bloqueada con candado y el texto «Obligatorio»", async () => {
    await abrir();

    const fasecolda = screen.getByRole("checkbox", { name: /fasecolda/i });
    expect(fasecolda).toBeChecked();
    expect(fasecolda).toBeDisabled();
    expect(screen.getByText("Obligatorio")).toBeInTheDocument();
  });

  it("el failover queda plegado en «Opciones avanzadas» con el valor resumido", async () => {
    const user = await abrir();

    const avanzadas = screen.getByRole("button", { name: /opciones avanzadas/i });
    expect(avanzadas).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("Failover 60000 ms")).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /timeout de failover/i })).not.toBeInTheDocument();
    await user.click(avanzadas);
    expect(screen.getByRole("textbox", { name: /timeout de failover/i })).toBeVisible();
  });
});
