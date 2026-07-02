// HU #10469 — Formulario de captura del módulo "Generación de improntas".
// HU #10471 — Descarga del PDF y estados de UI (vacío/cargando/error/éxito) por tipo de error.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ImprontaFormPanel } from "../ImprontaFormPanel";
import { ApiError } from "@/lib/api/types";

vi.mock("@/lib/api/client", () => ({
  getToken: vi.fn().mockReturnValue("fake-token"),
}));

vi.mock("@/lib/auth/jwt", () => ({
  decodeJwtPayload: vi.fn().mockReturnValue({
    tenant_name: "Renting Demo S.A.S.",
    display_name: "Ana Operadora",
    email: "ana@example.com",
  }),
}));

vi.mock("@/lib/api/admin-improntas", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api/admin-improntas")>(
    "@/lib/api/admin-improntas",
  );
  return {
    // `describeImprontaError` se conserva real: es la función bajo prueba en los
    // casos de error por tipo (AC2). Solo `generarImpronta` se mockea.
    describeImprontaError: actual.describeImprontaError,
    generarImpronta: vi.fn(),
  };
});

import { generarImpronta } from "@/lib/api/admin-improntas";

function fillOrgAndOperador() {
  // orgNombre y operador ya vienen pre-cargados desde la sesión (tenant_name/display_name);
  // solo hace falta completar NIT y ciudad para dejar el formulario listo para enviar.
  return {
    orgNit: screen.getByLabelText(/^NIT/i),
    orgCiudad: screen.getByLabelText(/^Ciudad/i),
  };
}

async function fillValidFormAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/^Placa/i), "abc123");
  await user.type(screen.getByLabelText(/Número de motor/i), "MTR-1");
  const { orgNit, orgCiudad } = fillOrgAndOperador();
  await user.type(orgNit, "900123456-7");
  await user.type(orgCiudad, "Bogotá D.C.");
  await user.click(screen.getByRole("button", { name: /Generar impronta/i }));
}

describe("ImprontaFormPanel — HU #10469", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1 pre-carga orgNombre y operador desde la sesión (tenant_name/display_name), editables", () => {
    render(<ImprontaFormPanel />);
    expect(screen.getByLabelText(/Nombre de la organización/i)).toHaveValue("Renting Demo S.A.S.");
    expect(screen.getByLabelText(/^Operador/i)).toHaveValue("Ana Operadora");
  });

  it("AC3 bloquea el envío y muestra error específico si placa y los tres identificadores están vacíos, sin invocar al backend", async () => {
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(
      await screen.findByText(/La placa es obligatoria\./i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Debes diligenciar al menos un identificador del vehículo/i),
    ).toBeInTheDocument();
    expect(generarImpronta).not.toHaveBeenCalled();
  });

  it("AC3 bloquea el envío si la placa está diligenciada pero motor/chasis/serie están vacíos", async () => {
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "ABC123");
    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    expect(
      await screen.findByText(/Debes diligenciar al menos un identificador del vehículo/i),
    ).toBeInTheDocument();
    expect(generarImpronta).not.toHaveBeenCalled();
  });
});

describe("ImprontaFormPanel — HU #10471", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("AC1 envía la solicitud, dispara la descarga (generarImpronta) y muestra éxito con radicado y hash visibles", async () => {
    vi.mocked(generarImpronta).mockResolvedValue({
      radicado: "IMPR-A1B2C3D4",
      hash: "9f1c...e0a2",
    });
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await fillValidFormAndSubmit(user);

    await waitFor(() => expect(generarImpronta).toHaveBeenCalledTimes(1));
    expect(generarImpronta).toHaveBeenCalledWith(
      expect.objectContaining({
        placa: "ABC123",
        numMotor: "MTR-1",
        orgNit: "900123456-7",
        orgCiudad: "Bogotá D.C.",
        operador: "Ana Operadora",
      }),
    );

    const success = await screen.findByTestId("impronta-success");
    expect(success).toBeInTheDocument();
    expect(screen.getByTestId("impronta-radicado")).toHaveTextContent("IMPR-A1B2C3D4");
    expect(screen.getByTestId("impronta-hash")).toHaveTextContent("9f1c...e0a2");
  });

  it("AC1 muestra un mensaje de éxito genérico cuando el backend no expone radicado/hash en headers", async () => {
    vi.mocked(generarImpronta).mockResolvedValue({ radicado: null, hash: null });
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await fillValidFormAndSubmit(user);

    expect(await screen.findByTestId("impronta-success")).toHaveTextContent(
      /Impronta generada y descargada/i,
    );
    expect(screen.queryByTestId("impronta-radicado")).not.toBeInTheDocument();
    expect(screen.queryByTestId("impronta-hash")).not.toBeInTheDocument();
  });

  it("AC2 muestra un mensaje específico de validación (422 VALIDATION_ERROR) con el detalle de campo del backend", async () => {
    vi.mocked(generarImpronta).mockRejectedValue(
      new ApiError(422, "Error 422", {
        code: "VALIDATION_ERROR",
        errors: [{ field: "placa", message: "La placa no tiene un formato válido." }],
      }),
    );
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await fillValidFormAndSubmit(user);

    expect(await screen.findByTestId("impronta-error")).toHaveTextContent(
      /La placa no tiene un formato válido\./i,
    );
  });

  it("AC2 muestra un mensaje de 'no disponible' reintentable cuando el backend responde UPSTREAM_UNAVAILABLE (502)", async () => {
    vi.mocked(generarImpronta).mockRejectedValue(
      new ApiError(502, "Error 502", { code: "UPSTREAM_UNAVAILABLE" }),
    );
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await fillValidFormAndSubmit(user);

    const error = await screen.findByTestId("impronta-error");
    expect(error).toHaveTextContent(/no está disponible/i);
    expect(error).toHaveTextContent(/intenta de nuevo/i);
  });

  it("AC2 muestra un mensaje distinto y comprensible cuando el backend responde UNAUTHORIZED (401), sin exponer detalle del proveedor", async () => {
    vi.mocked(generarImpronta).mockRejectedValue(
      new ApiError(401, "Error 401", {
        code: "UNAUTHORIZED",
        message: "invalid api key kr_live_9f8e7d6c5b4a scope impronta:generar",
      }),
    );
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await fillValidFormAndSubmit(user);

    const error = await screen.findByTestId("impronta-error");
    expect(error).toHaveTextContent(/No autorizado/i);
    expect(error).not.toHaveTextContent(/kr_live/i);
  });

  it("AC2 muestra un mensaje de fallback genérico ante un error de red/desconocido", async () => {
    vi.mocked(generarImpronta).mockRejectedValue(new TypeError("Failed to fetch"));
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await fillValidFormAndSubmit(user);

    expect(await screen.findByTestId("impronta-error")).toHaveTextContent(
      /No se pudo conectar con el servicio de improntas/i,
    );
  });

  it("AC3 muestra el estado de cargando (botón deshabilitado, indicación de que puede tardar) mientras la solicitud está en curso", async () => {
    let resolveCall: (value: { radicado: string | null; hash: string | null }) => void = () => {};
    vi.mocked(generarImpronta).mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveCall = resolve;
        }),
    );
    const user = userEvent.setup();
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "abc123");
    await user.type(screen.getByLabelText(/Número de motor/i), "MTR-1");
    const { orgNit, orgCiudad } = fillOrgAndOperador();
    await user.type(orgNit, "900123456-7");
    await user.type(orgCiudad, "Bogotá D.C.");
    await user.click(screen.getByRole("button", { name: /Generar impronta/i }));

    const loading = await screen.findByTestId("impronta-loading");
    expect(loading).toHaveTextContent(/puede tardar/i);
    expect(screen.getByRole("button", { name: /Generando impronta/i })).toBeDisabled();

    resolveCall({ radicado: null, hash: null });
    await waitFor(() => expect(screen.queryByTestId("impronta-loading")).not.toBeInTheDocument());
  });

  it("AC3 no permite un doble envío concurrente mientras la solicitud está en curso", async () => {
    let resolveCall: (value: { radicado: string | null; hash: string | null }) => void = () => {};
    vi.mocked(generarImpronta).mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveCall = resolve;
        }),
    );
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<ImprontaFormPanel />);

    await user.type(screen.getByLabelText(/^Placa/i), "abc123");
    await user.type(screen.getByLabelText(/Número de motor/i), "MTR-1");
    const { orgNit, orgCiudad } = fillOrgAndOperador();
    await user.type(orgNit, "900123456-7");
    await user.type(orgCiudad, "Bogotá D.C.");

    const button = screen.getByRole("button", { name: /Generar impronta/i });
    await user.click(button);
    await user.click(button);
    await user.click(button);

    expect(generarImpronta).toHaveBeenCalledTimes(1);
    resolveCall({ radicado: null, hash: null });
    await waitFor(() => expect(screen.queryByTestId("impronta-loading")).not.toBeInTheDocument());
  });
});
