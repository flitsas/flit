// HU #14001 (Feature #13996) — filtros por columna en el Baúl de firmas (estándar HU #13997).
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ToastProvider } from "@/components/admin/Toast";
import { SignatureVaultTab } from "../SignatureVaultTab";
import type { SignatureVaultItem } from "@/lib/api/admin-signature-vault";

vi.mock("@/lib/api/admin-signature-vault", () => ({
  fetchSignatureVault: vi.fn(),
  createSignatureVaultEntry: vi.fn(),
  revokeSignatureVaultEntry: vi.fn(),
}));

import { fetchSignatureVault } from "@/lib/api/admin-signature-vault";

function firma(over: Partial<SignatureVaultItem>): SignatureVaultItem {
  return {
    id: "x",
    documentType: "CC",
    documentNumber: "1000",
    fullName: "Nombre",
    codigoHash: "HASH",
    vigenciaDesde: "2026-01-01",
    vigenciaHasta: "2026-12-31",
    estado: "activa",
    fechaRegistro: "2026-06-01",
    ...over,
  };
}

const ANA = firma({
  id: "1",
  fullName: "Ana Gómez",
  documentNumber: "1098765432",
  codigoHash: "AB12CD34",
  fechaRegistro: "2026-03-15",
  vigenciaHasta: "2026-12-31",
  estado: "activa",
});
const LUIS = firma({
  id: "2",
  fullName: "Luis Pérez",
  documentNumber: "52111222",
  codigoHash: "ZZ99YY88",
  fechaRegistro: "2026-04-20",
  vigenciaHasta: "2026-03-10",
  estado: "vencida",
});
const MARTA = firma({
  id: "3",
  fullName: "Marta Rojas",
  documentNumber: "79333444",
  codigoHash: null,
  fechaRegistro: "2026-05-02",
  vigenciaHasta: "2026-08-01",
  estado: "revocada",
});

type User = ReturnType<typeof userEvent.setup>;

async function renderTab() {
  render(
    <ToastProvider>
      <SignatureVaultTab tenantId="t-1" />
    </ToastProvider>,
  );
  await screen.findByText("Ana Gómez");
}

async function abrirFiltro(user: User, columna: string) {
  await user.click(screen.getByRole("button", { name: /^\+ Filtro/ }));
  await user.click(screen.getByRole("button", { name: columna }));
}

async function filtrarTexto(user: User, columna: string, valor: string) {
  await abrirFiltro(user, columna);
  await user.type(screen.getByLabelText(new RegExp(`${columna} contiene`, "i")), valor);
  await user.click(screen.getByRole("button", { name: "Aplicar" }));
}

const visibles = () => screen.queryAllByRole("row").length - 1;

describe("SignatureVaultTab · filtros por columna (HU #14001)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchSignatureVault).mockResolvedValue([ANA, LUIS, MARTA]);
  });

  it("AC2: filtra por Registro (la fecha tal como se ve)", async () => {
    const user = userEvent.setup();
    await renderTab();
    await filtrarTexto(user, "Registro", "20/04/2026");
    expect(screen.getByText("Luis Pérez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC2: filtra por Documento", async () => {
    const user = userEvent.setup();
    await renderTab();
    await filtrarTexto(user, "Documento", "1098765");
    expect(screen.getByText("Ana Gómez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC2: filtra por Código hash", async () => {
    const user = userEvent.setup();
    await renderTab();
    await filtrarTexto(user, "Código hash", "zz99");
    expect(screen.getByText("Luis Pérez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC2: filtra por Apoderado", async () => {
    const user = userEvent.setup();
    await renderTab();
    await filtrarTexto(user, "Apoderado", "rojas");
    expect(screen.getByText("Marta Rojas")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC2: filtra por Estado", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Estado");
    await user.click(screen.getByRole("checkbox", { name: "Vencida" }));
    await user.click(screen.getByRole("checkbox", { name: "Revocada" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.queryByText("Ana Gómez")).not.toBeInTheDocument();
    expect(visibles()).toBe(2);
  });

  it("AC2: Vigencia (Periodo) actúa sobre la fecha de vencimiento", async () => {
    const user = userEvent.setup();
    await renderTab();
    await user.click(screen.getByRole("button", { name: /periodo/i }));
    await user.selectOptions(screen.getByLabelText("Periodo"), "Rango propio");
    await user.type(screen.getByLabelText("Fecha inicial del rango propio"), "2026-07-01");
    await user.type(screen.getByLabelText("Fecha final del rango propio"), "2026-09-30");
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(screen.getByText("Marta Rojas")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC2: combina varios filtros con AND", async () => {
    const user = userEvent.setup();
    await renderTab();
    await abrirFiltro(user, "Estado");
    await user.click(screen.getByRole("checkbox", { name: "Vencida" }));
    await user.click(screen.getByRole("checkbox", { name: "Activa" }));
    await user.click(screen.getByRole("button", { name: "Aplicar" }));
    expect(visibles()).toBe(2);
    await filtrarTexto(user, "Apoderado", "luis");
    expect(screen.getByText("Luis Pérez")).toBeInTheDocument();
    expect(visibles()).toBe(1);
  });

  it("AC3: sin resultados muestra el estado vacío; «Limpiar filtros» devuelve la lista completa", async () => {
    const user = userEvent.setup();
    await renderTab();
    await filtrarTexto(user, "Apoderado", "zzz");
    expect(screen.getByText(/ningún registro coincide/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Limpiar filtros" }));
    expect(visibles()).toBe(3);
  });
});
