// HU #12224 — pantalla de carga masiva.
//
// El backend de lotes llevaba tiempo completo y era inalcanzable: no existía un solo `input
// type="file"` en la aplicación. Estos casos cubren lo que hace utilizable esa API y, sobre todo,
// las dos formas de hacer daño desde aquí: subir dos veces el mismo lote —cien documentos
// duplicados que alguien tiene que borrar a mano— y devolver un código crudo en pantalla cuando el
// archivo se rechaza, que deja al usuario justo donde estaba.
import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mocks = vi.hoisted(() => ({
  createStandaloneBatch: vi.fn(),
  downloadStandaloneBatchTemplate: vi.fn(),
  push: vi.fn(),
}));

vi.mock("@/lib/api/admin-generacion-documental", () => ({
  createStandaloneBatch: mocks.createStandaloneBatch,
  downloadStandaloneBatchTemplate: mocks.downloadStandaloneBatchTemplate,
}));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: mocks.push }) }));

import { ApiError } from "@/lib/api/types";
import { LoteCargaPanel } from "../LoteCargaPanel";

const XLSX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

function plantilla(nombre = "lote.xlsx"): File {
  return new File(["contenido"], nombre, { type: XLSX });
}

/** Archivo y clave con los que se invocó la carga. */
function ultimaCarga(): { archivo: File; clave: string } {
  const calls = mocks.createStandaloneBatch.mock.calls;
  const [archivo, clave] = calls[calls.length - 1] as [File, string];
  return { archivo, clave };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.downloadStandaloneBatchTemplate.mockResolvedValue(undefined);
  mocks.createStandaloneBatch.mockResolvedValue({
    batchId: "11111111-1111-1111-1111-111111111111",
    status: "queued",
    total: 3,
    alreadyExisted: false,
  });
});

describe("Carga masiva — la plantilla se pide al servidor", () => {
  it("descarga la plantilla al pulsar el botón", async () => {
    render(<LoteCargaPanel />);

    await userEvent.click(screen.getByTestId("lote-descargar-plantilla"));

    expect(mocks.downloadStandaloneBatchTemplate).toHaveBeenCalledTimes(1);
  });

  it("si la descarga falla, lo dice y no rompe la pantalla", async () => {
    mocks.downloadStandaloneBatchTemplate.mockRejectedValue(new Error("500"));
    render(<LoteCargaPanel />);

    await userEvent.click(screen.getByTestId("lote-descargar-plantilla"));

    expect(await screen.findByTestId("lote-error")).toHaveTextContent(/no se pudo descargar/i);
    // El paso 2 sigue disponible: un fallo al descargar no impide subir un archivo que ya se tenía.
    expect(screen.getByTestId("lote-archivo")).toBeEnabled();
  });
});

describe("Carga masiva — qué se deja subir", () => {
  it("sin archivo, la carga está deshabilitada", () => {
    render(<LoteCargaPanel />);

    expect(screen.getByTestId("lote-cargar")).toBeDisabled();
  });

  it("un archivo que no es .xlsx se rechaza antes de subirlo", async () => {
    render(<LoteCargaPanel />);

    // `applyAccept: false` porque el atributo `accept` NO es un control: en el diálogo del sistema
    // el usuario puede pasar el filtro a «todos los archivos» y elegir lo que quiera. La comprobación
    // del componente es la que tiene que sostenerse.
    await userEvent.upload(
      screen.getByTestId("lote-archivo"),
      new File(["a;b"], "datos.csv", { type: "text/csv" }),
      { applyAccept: false },
    );

    expect(await screen.findByTestId("lote-error")).toHaveTextContent(/solo se admiten archivos \.xlsx/i);
    // Lo importante: no se gastó una subida ni se ocupó al worker con algo que iba a rechazar.
    expect(mocks.createStandaloneBatch).not.toHaveBeenCalled();
    expect(screen.getByTestId("lote-cargar")).toBeDisabled();
  });

  it("con un .xlsx válido se habilita la carga y se muestra qué se va a subir", async () => {
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla("mi-lote.xlsx"));

    expect(await screen.findByTestId("lote-archivo-elegido")).toHaveTextContent("mi-lote.xlsx");
    expect(screen.getByTestId("lote-cargar")).toBeEnabled();
    expect(screen.queryByTestId("lote-error")).not.toBeInTheDocument();
  });
});

describe("Carga masiva — la subida", () => {
  it("envía el archivo con una clave de idempotencia y lleva al seguimiento del lote", async () => {
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla());
    await userEvent.click(screen.getByTestId("lote-cargar"));

    await waitFor(() => expect(mocks.createStandaloneBatch).toHaveBeenCalledTimes(1));

    const { archivo, clave } = ultimaCarga();
    expect(archivo.name).toBe("lote.xlsx");
    expect(clave).toBeTruthy();

    expect(mocks.push).toHaveBeenCalledWith(
      "/admin/generacion-documental/lotes/11111111-1111-1111-1111-111111111111",
    );
  });

  it("reintentar el mismo archivo reutiliza la clave, así que no se duplica el lote", async () => {
    mocks.createStandaloneBatch.mockRejectedValueOnce(new Error("red caída"));
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla());

    await userEvent.click(screen.getByTestId("lote-cargar"));
    await screen.findByTestId("lote-error");
    const primera = ultimaCarga().clave;

    await userEvent.click(screen.getByTestId("lote-cargar"));
    await waitFor(() => expect(mocks.createStandaloneBatch).toHaveBeenCalledTimes(2));

    // Misma clave: para el backend es la MISMA solicitud, y responde con el lote que ya creó
    // (CF-16) en vez de generar cien documentos por segunda vez.
    expect(ultimaCarga().clave).toBe(primera);
  });

  it("elegir otro archivo genera una clave nueva: es otro lote", async () => {
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla("primero.xlsx"));
    await userEvent.click(screen.getByTestId("lote-cargar"));
    await waitFor(() => expect(mocks.createStandaloneBatch).toHaveBeenCalledTimes(1));
    const primera = ultimaCarga().clave;

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla("segundo.xlsx"));
    await userEvent.click(screen.getByTestId("lote-cargar"));
    await waitFor(() => expect(mocks.createStandaloneBatch).toHaveBeenCalledTimes(2));

    expect(ultimaCarga().clave).not.toBe(primera);
  });

  it("navega igual cuando el backend devuelve un lote que ya existía", async () => {
    mocks.createStandaloneBatch.mockResolvedValue({
      batchId: "22222222-2222-2222-2222-222222222222",
      status: "processing",
      total: 5,
      alreadyExisted: true,
    });
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla());
    await userEvent.click(screen.getByTestId("lote-cargar"));

    // Es el mismo lote: lo útil es ver su avance, no un aviso de que no se duplicó.
    await waitFor(() =>
      expect(mocks.push).toHaveBeenCalledWith(
        "/admin/generacion-documental/lotes/22222222-2222-2222-2222-222222222222",
      ),
    );
  });
});

describe("Carga masiva — los rechazos del archivo se explican, no se codifican", () => {
  it.each([
    ["too_many_rows", /no se procesó ninguna/i],
    ["template_invalid", /sin tocar la fila 1/i],
    ["invalid_file", /guardar como/i],
  ])("%s se traduce a una instrucción concreta", async (codigo, esperado) => {
    mocks.createStandaloneBatch.mockRejectedValue(
      new ApiError(422, codigo, { error: codigo, field: "file" }),
    );
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla());
    await userEvent.click(screen.getByTestId("lote-cargar"));

    const error = await screen.findByTestId("lote-error");
    expect(error).toHaveTextContent(esperado);
    // El código crudo no llega a la pantalla: no le dice nada a quien lo lee.
    expect(error).not.toHaveTextContent(codigo);
    expect(mocks.push).not.toHaveBeenCalled();
  });

  it("un 403 se explica como permiso, no como error del archivo", async () => {
    mocks.createStandaloneBatch.mockRejectedValue(new ApiError(403, "Forbidden", null));
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla());
    await userEvent.click(screen.getByTestId("lote-cargar"));

    expect(await screen.findByTestId("lote-error")).toHaveTextContent(/no tienes permiso/i);
  });

  it("tras un rechazo se puede reintentar: el botón vuelve a quedar disponible", async () => {
    mocks.createStandaloneBatch.mockRejectedValue(
      new ApiError(422, "invalid_file", { error: "invalid_file" }),
    );
    render(<LoteCargaPanel />);

    await userEvent.upload(screen.getByTestId("lote-archivo"), plantilla());
    await userEvent.click(screen.getByTestId("lote-cargar"));
    await screen.findByTestId("lote-error");

    expect(screen.getByTestId("lote-cargar")).toBeEnabled();
  });
});
