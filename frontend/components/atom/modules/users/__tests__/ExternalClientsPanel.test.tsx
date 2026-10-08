// HU #13200 — Submódulo «Clientes de integración» (clientes externos, p. ej. Flito). Cubre el listado con sus
// cuatro estados (AC1), el alta con el secreto mostrado una sola vez (AC2), los errores de alta sin perder lo
// escrito (AC3), la rotación con revocación opcional (AC4) y las acciones de operación (AC5). La API
// (HU #13088) es un doble; las reglas puras (formato, bloqueo) se usan reales.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ExternalClientsPanel } from "../ExternalClientsPanel";
import { ApiError } from "@/lib/api/types";
import {
  createExternalClient,
  fetchExternalClients,
  regenerateExternalClientSecret,
  unlockExternalClient,
  updateExternalClient,
  type ExternalClient,
} from "@/lib/api/external-clients";

vi.mock("@/lib/api/external-clients", async (importOriginal) => {
  const real = await importOriginal<typeof import("@/lib/api/external-clients")>();
  return {
    ...real,
    fetchExternalClients: vi.fn(),
    createExternalClient: vi.fn(),
    updateExternalClient: vi.fn(),
    regenerateExternalClientSecret: vi.fn(),
    unlockExternalClient: vi.fn(),
  };
});

const SECRETO = "secreto-de-prueba-0000000000000000000000000";

const flito: ExternalClient = {
  id: "client-1",
  clientId: "flito-dev",
  displayName: "Flito (DEV)",
  purpose: "Sincronización de trámites para procesos Flito",
  scopes: ["external.tramites.read", "external.tramites.pii.read"],
  isActive: true,
  mustRotate: false,
  lockedUntil: null,
  lastTokenAt: "2026-09-30T15:00:00Z",
  createdAt: "2026-09-29T10:00:00Z",
};

const bloqueado: ExternalClient = {
  ...flito,
  id: "client-2",
  clientId: "flito-qa",
  displayName: "Flito (QA)",
  scopes: ["external.tramites.read"],
  lockedUntil: "2999-01-01T00:00:00Z",
  lastTokenAt: null,
};

function fila(clientId: string) {
  return screen.getByRole("row", { name: new RegExp(clientId) });
}

describe("ExternalClientsPanel (#13200)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fetchExternalClients).mockResolvedValue([flito, bloqueado]);
  });

  it("AC1: lista los clientes con permisos, estado y último pase, sin ningún secreto", async () => {
    render(<ExternalClientsPanel />);

    const table = await screen.findByRole("table", { name: /clientes de integración externos/i });
    expect(within(table).getAllByRole("columnheader").map((h) => h.textContent)).toEqual([
      "Cliente", "Finalidad", "Permisos", "Estado", "Último pase", "Alta", "Acciones",
    ]);
    const f1 = fila("flito-dev");
    expect(within(f1).getByText("Flito (DEV)")).toBeInTheDocument();
    expect(within(f1).getByLabelText("Permiso: datos personales sin enmascarar")).toBeInTheDocument();
    expect(within(f1).getByLabelText("Estado: Activo")).toBeInTheDocument();
    const f2 = fila("flito-qa");
    expect(within(f2).getByLabelText(/sin permiso de datos personales/i)).toBeInTheDocument();
    expect(within(f2).getByLabelText(/bloqueado por intentos fallidos/i)).toBeInTheDocument();
    // El listado nunca trae secretos: no hay tarjeta de secreto ni nada que copiar.
    expect(screen.queryByRole("region", { name: /secreto de/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /copiar secreto/i })).not.toBeInTheDocument();
    expect(document.querySelector("code")).toBeNull();
  });

  it("AC1: estados vacío y error (con reintento)", async () => {
    vi.mocked(fetchExternalClients).mockResolvedValueOnce([]);
    const { unmount } = render(<ExternalClientsPanel />);
    expect(await screen.findByText(/no hay clientes de integración/i)).toBeInTheDocument();
    unmount();

    vi.mocked(fetchExternalClients).mockRejectedValueOnce(new Error("caído"));
    render(<ExternalClientsPanel />);
    expect(await screen.findByText(/no se pudieron cargar los clientes de integración/i)).toBeInTheDocument();
  });

  it("AC2: el alta muestra el secreto una sola vez, se puede copiar y al cerrar desaparece", async () => {
    const ue = userEvent.setup();
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });
    vi.mocked(createExternalClient).mockResolvedValue({
      client: { ...flito, id: "client-3", clientId: "flito-pdn", displayName: "Flito (PDN)" },
      clientSecret: SECRETO,
    });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /nuevo cliente/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(within(dialog).getByLabelText(/identificador/i), "flito-pdn");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "Flito (PDN)");
    await ue.type(within(dialog).getByLabelText(/finalidad/i), "Sincronización");
    await ue.click(within(dialog).getByLabelText(/datos personales sin enmascarar/i));
    await ue.click(screen.getByRole("button", { name: /crear cliente/i }));

    await waitFor(() =>
      expect(createExternalClient).toHaveBeenCalledWith({
        clientId: "flito-pdn",
        displayName: "Flito (PDN)",
        purpose: "Sincronización",
        scopes: ["external.tramites.read", "external.tramites.pii.read"],
      }),
    );
    expect(await screen.findByText(SECRETO)).toBeInTheDocument();
    expect(screen.getByText(/no se volverá a mostrar/i)).toBeInTheDocument();
    expect(fila("flito-pdn")).toBeInTheDocument();

    await ue.click(screen.getByRole("button", { name: /copiar secreto/i }));
    expect(writeText).toHaveBeenCalledWith(SECRETO);

    await ue.click(screen.getByRole("button", { name: /cerrar: el secreto no se volverá a mostrar/i }));
    expect(screen.queryByText(SECRETO)).not.toBeInTheDocument();
    expect(window.localStorage.length + window.sessionStorage.length).toBe(0);
  });

  it("AC3: identificador ya usado → mensaje claro y el formulario conserva lo escrito", async () => {
    const ue = userEvent.setup();
    vi.mocked(createExternalClient).mockRejectedValue(
      new ApiError(409, "Conflict", { error: "client_id_taken", message: "…" }),
    );
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /nuevo cliente/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(within(dialog).getByLabelText(/identificador/i), "flito-dev");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "Otro Flito");
    await ue.type(within(dialog).getByLabelText(/finalidad/i), "Pruebas");
    await ue.click(screen.getByRole("button", { name: /crear cliente/i }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/no se reutilizan/i);
    expect(within(dialog).getByLabelText(/identificador/i)).toHaveValue("flito-dev");
    expect(within(dialog).getByLabelText(/^nombre/i)).toHaveValue("Otro Flito");
  });

  it("AC3: identificador con formato inválido → no llama a la API", async () => {
    const ue = userEvent.setup();
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /nuevo cliente/i }));
    const dialog = await screen.findByRole("dialog");
    await ue.type(within(dialog).getByLabelText(/identificador/i), "fl");
    await ue.type(within(dialog).getByLabelText(/^nombre/i), "X");
    await ue.type(within(dialog).getByLabelText(/finalidad/i), "Y");
    await ue.click(screen.getByRole("button", { name: /crear cliente/i }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(/minúsculas, dígitos y guiones/i);
    expect(createExternalClient).not.toHaveBeenCalled();
  });

  it("AC4: regenerar con «revocar de inmediato» avisa de los pases vigentes y muestra el secreto nuevo una vez", async () => {
    const ue = userEvent.setup();
    vi.mocked(regenerateExternalClientSecret).mockResolvedValue({ client: flito, clientSecret: SECRETO });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /regenerar secreto de flito-dev/i }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/máximo 30 minutos/i)).toBeInTheDocument();
    await ue.click(within(dialog).getByLabelText(/revocar el secreto anterior de inmediato/i));
    await ue.click(within(dialog).getByRole("button", { name: /^regenerar secreto$/i }));

    await waitFor(() => expect(regenerateExternalClientSecret).toHaveBeenCalledWith("client-1", true));
    expect(await screen.findByText(SECRETO)).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("AC5: «Desbloquear» solo aparece en el cliente bloqueado y lo desbloquea tras confirmar", async () => {
    const ue = userEvent.setup();
    vi.mocked(unlockExternalClient).mockResolvedValue({ ...bloqueado, lockedUntil: null });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    expect(within(fila("flito-dev")).queryByRole("button", { name: /desbloquear/i })).not.toBeInTheDocument();
    await ue.click(within(fila("flito-qa")).getByRole("button", { name: /desbloquear flito-qa/i }));
    await ue.click(within(await screen.findByRole("dialog")).getByRole("button", { name: /^desbloquear$/i }));

    await waitFor(() => expect(unlockExternalClient).toHaveBeenCalledWith("client-2"));
    await waitFor(() =>
      expect(within(fila("flito-qa")).queryByLabelText(/bloqueado por intentos fallidos/i)).not.toBeInTheDocument(),
    );
  });

  it("AC5: desactivar y obligar a rotar piden confirmación, avisan de los pases vigentes y actualizan el estado", async () => {
    const ue = userEvent.setup();
    vi.mocked(updateExternalClient)
      .mockResolvedValueOnce({ ...flito, isActive: false })
      .mockResolvedValueOnce({ ...flito, isActive: false, mustRotate: true });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /desactivar flito-dev/i }));
    let dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/máximo 30 minutos/i)).toBeInTheDocument();
    await ue.click(within(dialog).getByRole("button", { name: /^desactivar$/i }));
    await waitFor(() => expect(updateExternalClient).toHaveBeenCalledWith("client-1", { isActive: false }));
    expect(await within(fila("flito-dev")).findByLabelText("Estado: Inactivo")).toBeInTheDocument();

    await ue.click(screen.getByRole("button", { name: /obligar a rotar el secreto de flito-dev/i }));
    dialog = await screen.findByRole("dialog");
    await ue.click(within(dialog).getByRole("button", { name: /^obligar a rotar$/i }));
    await waitFor(() => expect(updateExternalClient).toHaveBeenLastCalledWith("client-1", { mustRotate: true }));
    expect(await within(fila("flito-dev")).findByLabelText(/debe rotar el secreto/i)).toBeInTheDocument();
  });

  it("AC5: la edición no permite cambiar el identificador y guarda nombre, finalidad y permisos", async () => {
    const ue = userEvent.setup();
    vi.mocked(updateExternalClient).mockResolvedValue({ ...flito, displayName: "Flito DEV 2", scopes: ["external.tramites.read"] });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");

    await ue.click(screen.getByRole("button", { name: /editar flito-dev/i }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(/identificador/i)).toBeDisabled();
    const nombre = within(dialog).getByLabelText(/^nombre/i);
    await ue.clear(nombre);
    await ue.type(nombre, "Flito DEV 2");
    await ue.click(within(dialog).getByLabelText(/datos personales sin enmascarar/i));
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(updateExternalClient).toHaveBeenCalledWith("client-1", {
        displayName: "Flito DEV 2",
        purpose: flito.purpose,
        scopes: ["external.tramites.read"],
      }),
    );
    expect(await within(fila("flito-dev")).findByText("Flito DEV 2")).toBeInTheDocument();
  });

  // Feature #13261: el permiso de envío de adjuntos (comprobante de impuesto) se ve, se concede y no se pierde al editar.
  it("#13261: el panel muestra el permiso de adjuntos y la edición lo conserva junto con permisos desconocidos", async () => {
    const ue = userEvent.setup();
    const conAdjuntos: ExternalClient = {
      ...flito,
      scopes: [
        "external.tramites.read",
        "external.tramites.pii.read",
        "external.tramites.attachments.write",
        "external.tramites.futuro",
      ],
    };
    vi.mocked(fetchExternalClients).mockResolvedValue([conAdjuntos]);
    vi.mocked(updateExternalClient).mockResolvedValue({ ...conAdjuntos, displayName: "Flito DEV 2" });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");
    expect(within(fila("flito-dev")).getByLabelText("Permiso: envío de adjuntos")).toBeInTheDocument();

    await ue.click(screen.getByRole("button", { name: /editar flito-dev/i }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(/envío de adjuntos/i)).toBeChecked();
    const nombre = within(dialog).getByLabelText(/^nombre/i);
    await ue.clear(nombre);
    await ue.type(nombre, "Flito DEV 2");
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(updateExternalClient).toHaveBeenCalledWith("client-1", {
        displayName: "Flito DEV 2",
        purpose: flito.purpose,
        scopes: [
          "external.tramites.read",
          "external.tramites.pii.read",
          "external.tramites.attachments.write",
          "external.tramites.futuro",
        ],
      }),
    );
  });

  it("#13261: la edición concede el permiso de adjuntos al marcarlo", async () => {
    const ue = userEvent.setup();
    vi.mocked(updateExternalClient).mockResolvedValue({
      ...flito,
      scopes: [...flito.scopes, "external.tramites.attachments.write"],
    });
    render(<ExternalClientsPanel />);
    await screen.findByRole("table");
    expect(within(fila("flito-dev")).queryByLabelText("Permiso: envío de adjuntos")).not.toBeInTheDocument();

    await ue.click(screen.getByRole("button", { name: /editar flito-dev/i }));
    const dialog = await screen.findByRole("dialog");
    const adjuntos = within(dialog).getByLabelText(/envío de adjuntos/i);
    expect(adjuntos).not.toBeChecked();
    await ue.click(adjuntos);
    await ue.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() =>
      expect(updateExternalClient).toHaveBeenCalledWith("client-1", {
        displayName: flito.displayName,
        purpose: flito.purpose,
        scopes: ["external.tramites.read", "external.tramites.pii.read", "external.tramites.attachments.write"],
      }),
    );
    expect(await within(fila("flito-dev")).findByLabelText("Permiso: envío de adjuntos")).toBeInTheDocument();
  });
});
