// HU #14005 — Filtros por columna en Auditoría (servidor). AC1 (params + página 1), AC2 (rango
// invertido sin consulta / 422), AC3 (vacío con «Limpiar filtros»).
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AuditLogPanel } from "../panels/AuditLogPanel";
import { ApiValidationError, type AuditLogPageResponse } from "@/lib/api/types";

const fetchAuditLog = vi.fn();
vi.mock("@/lib/api/admin-companies", () => ({
  fetchAuditLog: (...args: unknown[]) => fetchAuditLog(...args),
}));

const FULL: AuditLogPageResponse = {
  data: [
    {
      entityName: "tenant_operational_policies",
      fieldName: "generate_improntas",
      oldValue: "false",
      newValue: "true",
      changedBy: "aaaaaaaa-1111",
      changedAt: "2026-03-01T09:00:00Z",
    },
  ],
  totalCount: 1,
  page: 1,
  pageSize: 10,
};
const EMPTY: AuditLogPageResponse = { data: [], totalCount: 0, page: 1, pageSize: 10 };

const ultimoLlamado = () => fetchAuditLog.mock.calls[fetchAuditLog.mock.calls.length - 1];

async function aplicarTexto(columna: string, etiqueta: RegExp, valor: string) {
  await userEvent.click(screen.getByRole("button", { name: /\+ filtro/i }));
  const dialog = screen.getByRole("dialog", { name: "Filtros de la tabla" });
  await userEvent.click(within(dialog).getByRole("button", { name: columna }));
  await userEvent.type(within(dialog).getByLabelText(etiqueta), valor);
  await userEvent.click(within(dialog).getByRole("button", { name: "Aplicar" }));
}

async function periodoRango(desde: string, hasta: string) {
  await userEvent.click(screen.getByRole("button", { name: /periodo/i }));
  const dialog = screen.getByRole("dialog", { name: "Elegir periodo" });
  await userEvent.selectOptions(within(dialog).getByRole("combobox"), "Rango propio");
  await userEvent.type(within(dialog).getByLabelText("Fecha inicial del rango propio"), desde);
  await userEvent.type(within(dialog).getByLabelText("Fecha final del rango propio"), hasta);
  await userEvent.click(within(dialog).getByRole("button", { name: "Aplicar" }));
}

describe("AuditLogPanel — filtros por columna (HU #14005)", () => {
  beforeEach(() => {
    fetchAuditLog.mockReset();
    window.localStorage.clear();
  });

  it("AC1: Campo modificado / Valor anterior / Operador viajan como params combinados", async () => {
    fetchAuditLog.mockResolvedValue(FULL);
    render(<AuditLogPanel tenantId="t-1" networkHeadId="h-1" />);
    await screen.findByText("Generación de improntas");
    expect(ultimoLlamado()[5]).toEqual({});

    await aplicarTexto("Campo modificado", /campo modificado contiene/i, "improntas");
    await waitFor(() => expect(ultimoLlamado()[5]).toEqual({ campo: "improntas" }));
    await aplicarTexto("Valor anterior", /valor anterior contiene/i, "false");
    await aplicarTexto("Operador", /operador contiene/i, "aaaa");
    await waitFor(() =>
      expect(ultimoLlamado()[5]).toEqual({
        campo: "improntas",
        valorAnterior: "false",
        operador: "aaaa",
      }),
    );
    expect(ultimoLlamado()[1]).toBe(1);
    expect(ultimoLlamado()[4]).toBe("h-1");
  });

  it("AC1: el Periodo (Rango propio) manda fechaDesde/fechaHasta y vuelve a la página 1", async () => {
    fetchAuditLog.mockResolvedValue(FULL);
    render(<AuditLogPanel tenantId="t-1" />);
    await screen.findByText("Generación de improntas");
    await periodoRango("2026-03-01", "2026-03-31");
    await waitFor(() =>
      expect(ultimoLlamado()[5]).toEqual({ fechaDesde: "2026-03-01", fechaHasta: "2026-03-31" }),
    );
    expect(ultimoLlamado()[1]).toBe(1);
  });

  it("AC1: aplicar un filtro estando en la página 2 consulta la página 1", async () => {
    fetchAuditLog.mockResolvedValue({ ...FULL, totalCount: 25, pageSize: 10 });
    render(<AuditLogPanel tenantId="t-1" />);
    await screen.findByText("Generación de improntas");
    await userEvent.click(screen.getByRole("button", { name: /página siguiente/i }));
    await waitFor(() => expect(ultimoLlamado()[1]).toBe(2));
    await aplicarTexto("Operador", /operador contiene/i, "bbb");
    await waitFor(() => expect(ultimoLlamado()[5]).toEqual({ operador: "bbb" }));
    expect(ultimoLlamado()[1]).toBe(1);
  });

  it("AC2: rango con fecha inicial posterior a la final muestra el error y NO consulta la API", async () => {
    fetchAuditLog.mockResolvedValue(FULL);
    render(<AuditLogPanel tenantId="t-1" />);
    await screen.findByText("Generación de improntas");
    const llamadas = fetchAuditLog.mock.calls.length;
    await periodoRango("2026-04-10", "2026-04-01");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      /fecha inicial no puede ser posterior/i,
    );
    expect(fetchAuditLog.mock.calls.length).toBe(llamadas);
  });

  it("AC2: si llega un 422, se muestra su mensaje", async () => {
    fetchAuditLog.mockResolvedValueOnce(FULL);
    fetchAuditLog.mockRejectedValueOnce(
      new ApiValidationError(
        [{ field: "fechaDesde", message: "La fecha desde es posterior a la fecha hasta." }],
        422,
      ),
    );
    render(<AuditLogPanel tenantId="t-1" />);
    await screen.findByText("Generación de improntas");
    await aplicarTexto("Operador", /operador contiene/i, "x");
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "La fecha desde es posterior a la fecha hasta.",
    );
  });

  it("AC3: sin resultados muestra el vacío con «Limpiar filtros», que restablece la consulta", async () => {
    fetchAuditLog.mockResolvedValueOnce(FULL);
    fetchAuditLog.mockResolvedValueOnce(EMPTY);
    fetchAuditLog.mockResolvedValue(FULL);
    render(<AuditLogPanel tenantId="t-1" />);
    await screen.findByText("Generación de improntas");
    await aplicarTexto("Operador", /operador contiene/i, "zzz");
    await screen.findByText("Ningún registro coincide con los filtros");
    await userEvent.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    await screen.findByText("Generación de improntas");
    expect(ultimoLlamado()[5]).toEqual({});
  });

  it("sin filtros y sin datos conserva el vacío propio de la compañía", async () => {
    fetchAuditLog.mockResolvedValue(EMPTY);
    render(<AuditLogPanel tenantId="t-1" />);
    expect(
      await screen.findByText(/aún no hay cambios registrados para esta compañía/i),
    ).toBeInTheDocument();
  });
});
