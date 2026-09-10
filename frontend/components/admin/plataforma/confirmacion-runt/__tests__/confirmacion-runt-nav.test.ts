// HU #12313 — cada pestaña de Confirmación RUNT se gatea por SU permiso, no por el del módulo.
// Uso de ejemplo: visibleConfirmacionRuntTabs(payload) → [{ id: "historial", … }]
import { describe, expect, it } from "vitest";
import type { JwtPayload } from "@/lib/auth/jwt";
import {
  canSeeConfirmacionRuntTab,
  confirmacionRuntTabPath,
  defaultConfirmacionRuntTab,
  visibleConfirmacionRuntTabs,
} from "../confirmacion-runt-nav";

const onlyHistory: JwtPayload = { role: "Auditor", permissions: ["runt_confirmation.history.read"] };
const onlySettings: JwtPayload = { role: "Operador", permissions: ["runt_confirmation.settings.manage"] };
const superAdmin: JwtPayload = { role: "SuperAdmin" };
const nobody: JwtPayload = { role: "AdminCompany", permissions: ["tramites.read"] };

describe("confirmacion-runt-nav", () => {
  it("con solo history.read ve Historial, aterriza en Historial y no ve Configuración (AC5)", () => {
    expect(visibleConfirmacionRuntTabs(onlyHistory).map((t) => t.id)).toEqual(["historial"]);
    expect(defaultConfirmacionRuntTab(onlyHistory)).toBe("historial");
    expect(canSeeConfirmacionRuntTab(onlyHistory, "configuracion")).toBe(false);
  });

  it("con solo settings.manage ve Configuración y no Historial (AC5)", () => {
    expect(visibleConfirmacionRuntTabs(onlySettings).map((t) => t.id)).toEqual(["configuracion"]);
    expect(defaultConfirmacionRuntTab(onlySettings)).toBe("configuracion");
    expect(canSeeConfirmacionRuntTab(onlySettings, "historial")).toBe(false);
  });

  it("SuperAdmin ve las dos, Configuración primero", () => {
    expect(visibleConfirmacionRuntTabs(superAdmin).map((t) => t.id)).toEqual(["configuracion", "historial"]);
    expect(defaultConfirmacionRuntTab(superAdmin)).toBe("configuracion");
  });

  it("sin ninguno de los dos permisos no hay pestañas ni aterrizaje (→ not-found)", () => {
    expect(visibleConfirmacionRuntTabs(nobody)).toEqual([]);
    expect(defaultConfirmacionRuntTab(nobody)).toBeNull();
    expect(defaultConfirmacionRuntTab(null)).toBeNull();
  });

  it("cada pestaña tiene ruta real bajo el segmento", () => {
    expect(confirmacionRuntTabPath("configuracion")).toBe("/admin/plataforma/confirmacion-runt/configuracion");
    expect(confirmacionRuntTabPath("historial")).toBe("/admin/plataforma/confirmacion-runt/historial");
  });
});
