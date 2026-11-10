// Feature #14420 / ADR-0077 — mensajes y CSV de errores de la invitación masiva (HU #14426).
import { describe, it, expect } from "vitest";
import { ApiError } from "@/lib/api/types";
import {
  BulkInvitationsNetworkError,
  type BulkInvitationRowResult,
} from "@/lib/api/bulk-invitations-client";
import { EMAIL_ALREADY_ASSOCIATED_MESSAGE } from "../emailConflict";
import {
  buildBulkInviteErrorsCsv,
  bulkInviteErrorRows,
  bulkInviteRequestErrorMessage,
  bulkInviteRowReason,
  bulkInviteTemplateErrorMessage,
  isBulkInviteOutcomeUncertain,
  neutralizeCsvFormula,
  validateBulkInviteFile,
} from "../bulkInviteErrors";

const row = (over: Partial<BulkInvitationRowResult>): BulkInvitationRowResult => ({
  rowNumber: 2,
  email: "a@x.co",
  fullName: "Ana",
  roleName: "Radicador",
  status: "rejected",
  errorCode: "EMAIL_ALREADY_IN_USE",
  ...over,
});

describe("validateBulkInviteFile", () => {
  it("acepta un .xlsx dentro del tope", () => {
    expect(validateBulkInviteFile(new File(["x"], "Usuarios.XLSX"))).toBeNull();
  });
  it("rechaza lo que no es .xlsx", () => {
    expect(validateBulkInviteFile(new File(["x"], "usuarios.csv"))).toMatch(/\.xlsx/);
  });
  it("rechaza archivos vacíos y de más de 1 MB", () => {
    expect(validateBulkInviteFile(new File([], "a.xlsx"))).toMatch(/vacío/i);
    const big = new File([new Uint8Array(1024 * 1024 + 1)], "a.xlsx");
    expect(validateBulkInviteFile(big)).toMatch(/1 MB/);
  });
});

describe("bulkInviteRequestErrorMessage", () => {
  it("traduce el código estable del rechazo del archivo (cualquier capitalización)", () => {
    expect(bulkInviteRequestErrorMessage(new ApiError(400, "x", { error: "too_many_rows" }))).toMatch(/50 filas/);
    expect(bulkInviteRequestErrorMessage(new ApiError(400, "x", { code: "TemplateInvalid" }))).toMatch(/plantilla/);
  });
  it("por status cuando no hay código conocido: 403, 429 y genérico", () => {
    expect(bulkInviteRequestErrorMessage(new ApiError(403, "x", null))).toMatch(/permiso/);
    expect(bulkInviteRequestErrorMessage(new ApiError(429, "x", null))).toMatch(/demasiadas/);
    expect(bulkInviteRequestErrorMessage(new ApiError(500, "x", null))).toMatch(/Inténtalo/);
  });
  it("nunca devuelve el mensaje técnico del backend ni la ruta", () => {
    expect(bulkInviteRequestErrorMessage(new ApiError(500, "SELECT * FROM users", null))).not.toMatch(/SELECT/);
  });
});

describe("bulkInviteTemplateErrorMessage", () => {
  it("distingue sin permiso del resto", () => {
    expect(bulkInviteTemplateErrorMessage(new ApiError(403, "x", null))).toMatch(/permiso/);
    expect(bulkInviteTemplateErrorMessage(new Error("boom"))).toMatch(/descargar la plantilla/);
  });
});

describe("isBulkInviteOutcomeUncertain", () => {
  it("red, timeout y 502/503/504 son inciertos; un 400 o 403 no", () => {
    expect(isBulkInviteOutcomeUncertain(new BulkInvitationsNetworkError("timeout"))).toBe(true);
    expect(isBulkInviteOutcomeUncertain(new BulkInvitationsNetworkError("network"))).toBe(true);
    expect(isBulkInviteOutcomeUncertain(new ApiError(504, "x", null))).toBe(true);
    expect(isBulkInviteOutcomeUncertain(new ApiError(400, "x", null))).toBe(false);
    expect(isBulkInviteOutcomeUncertain(new ApiError(403, "x", null))).toBe(false);
  });
});

describe("bulkInviteRowReason", () => {
  it("EMAIL_ALREADY_IN_USE usa el mensaje unificado (no distingue la causa)", () => {
    expect(bulkInviteRowReason(row({}))).toBe(EMAIL_ALREADY_ASSOCIATED_MESSAGE);
  });
  it("traduce códigos de rol y cae a un mensaje con el código si es desconocido", () => {
    expect(bulkInviteRowReason(row({ errorCode: "SUPERADMIN_ROLE_NOT_ASSIGNABLE" }))).toMatch(/Super Administrador/);
    expect(bulkInviteRowReason(row({ errorCode: "weird_new_code" }))).toContain("WEIRD_NEW_CODE");
  });
  it("una fila no procesada explica el deadline aunque no traiga código", () => {
    expect(bulkInviteRowReason(row({ status: "not_processed", errorCode: null }))).toMatch(/tiempo/);
  });
});

describe("neutralizeCsvFormula", () => {
  it.each(["=SUM(A1)", "+1", "-2", "@cmd", "\tx", "\rx"])("neutraliza %j", (value) => {
    expect(neutralizeCsvFormula(value).startsWith("'")).toBe(true);
  });
  it("no toca valores normales", () => {
    expect(neutralizeCsvFormula("ana@x.co")).toBe("ana@x.co");
  });
});

describe("buildBulkInviteErrorsCsv", () => {
  it("lleva BOM, separador ; y solo las filas rechazadas / no procesadas", () => {
    const csv = buildBulkInviteErrorsCsv([
      row({ rowNumber: 2, status: "valid", errorCode: null }),
      row({ rowNumber: 3 }),
      row({ rowNumber: 4, status: "not_processed", errorCode: null }),
    ]);

    expect(csv.startsWith("\uFEFF")).toBe(true);
    const lines = csv.slice(1).split("\r\n");
    expect(lines).toHaveLength(3);
    expect(lines[0]).toBe('"Fila";"Correo";"Nombre completo";"Rol";"Motivo"');
    expect(lines[1]).toContain('"3";"a@x.co"');
  });

  it("escapa fórmulas y comillas en los valores que vienen del archivo", () => {
    const csv = buildBulkInviteErrorsCsv([
      row({ email: '=HYPERLINK("http://evil")', fullName: "+cmd|' /C calc'!A0", roleName: '@SUM(1)' }),
    ]);
    const dataLine = csv.slice(1).split("\r\n")[1];

    expect(dataLine).toContain(`"'=HYPERLINK(""http://evil"")"`);
    expect(dataLine).toContain(`"'+cmd`);
    expect(dataLine).toContain(`"'@SUM(1)"`);
    // Ninguna celda arranca con un prefijo de fórmula.
    for (const cell of dataLine.split(";")) expect(cell).not.toMatch(/^"[=+\-@]/);
  });

  it("bulkInviteErrorRows ignora filas válidas y creadas", () => {
    expect(
      bulkInviteErrorRows([row({ status: "created", errorCode: null }), row({ status: "valid", errorCode: null })]),
    ).toEqual([]);
  });
});
