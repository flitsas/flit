// HU #14426 (Feature #14420 · ADR-0077) — asistente de invitación masiva por Excel:
// plantilla → .xlsx → resumen dryRun → confirmar → resultado + CSV de errores.
import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const downloadMock = vi.hoisted(() => vi.fn());
vi.mock("@/components/consultas/export", () => ({ download: downloadMock }));

import { BulkInviteModal, type BulkInviteModalProps } from "../BulkInviteModal";
import {
  BulkInvitationsNetworkError,
  type BulkInvitationReport,
  type BulkInvitationsClient,
} from "@/lib/api/bulk-invitations-client";
import { ApiError } from "@/lib/api/types";
import { EMAIL_ALREADY_ASSOCIATED_MESSAGE } from "@/lib/users/emailConflict";

const dryReport: BulkInvitationReport = {
  dryRun: true,
  total: 3,
  valid: 2,
  created: 0,
  rejected: 1,
  notProcessed: 0,
  rows: [
    { rowNumber: 2, email: "ana@x.co", fullName: "Ana", roleName: "Radicador", status: "valid", errorCode: null },
    { rowNumber: 3, email: "beto@x.co", fullName: "Beto", roleName: "Radicador", status: "valid", errorCode: null },
    { rowNumber: 4, email: "=cmd@x.co", fullName: "Caro", roleName: "Radicador", status: "rejected", errorCode: "EMAIL_ALREADY_IN_USE" },
  ],
};

const commitReport: BulkInvitationReport = {
  ...dryReport,
  dryRun: false,
  valid: 2,
  created: 2,
  rows: dryReport.rows.map((r) => (r.status === "valid" ? { ...r, status: "created" as const } : r)),
};

function makeClient(over: Partial<BulkInvitationsClient> = {}) {
  const client = {
    downloadTemplate: vi.fn().mockResolvedValue(undefined),
    submit: vi.fn(async (_f: File, o: { dryRun: boolean }) => (o.dryRun ? dryReport : commitReport)),
    ...over,
  };
  return client satisfies BulkInvitationsClient;
}

function renderModal(props: Partial<BulkInviteModalProps> = {}) {
  const client = (props.client ?? makeClient()) as BulkInvitationsClient;
  const onClose = vi.fn();
  const onCompleted = vi.fn();
  const utils = render(
    <BulkInviteModal client={client} contextKey="ctx-1" onClose={onClose} onCompleted={onCompleted} {...props} />,
  );
  return { ...utils, client, onClose, onCompleted };
}

const xlsx = (name = "usuarios.xlsx") => new File(["fake"], name);

async function pick(user: ReturnType<typeof userEvent.setup>, file: File) {
  await user.upload(screen.getByLabelText("Archivo Excel (.xlsx)"), file);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("BulkInviteModal — estado vacío y accesibilidad", () => {
  it("es un diálogo modal con nombre y descripción, y muestra los dos pasos sin archivo", () => {
    renderModal();

    const dialog = screen.getByRole("dialog", { name: /importar usuarios desde excel/i });
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveAccessibleDescription(/hasta 50 usuarios/i);
    expect(screen.getByRole("button", { name: /descargar plantilla/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeDisabled();
  });

  it("el foco queda contenido: Tab desde el último control vuelve al primero y Shift+Tab al revés", async () => {
    const user = userEvent.setup();
    renderModal();
    const dialog = screen.getByRole("dialog");
    const focusables = () =>
      Array.from(dialog.querySelectorAll<HTMLElement>("button:not([disabled]), input:not([disabled])"));

    const last = focusables().at(-1)!;
    last.focus();
    await user.tab();
    expect(document.activeElement).toBe(focusables()[0]);

    await user.tab({ shift: true });
    expect(document.activeElement).toBe(last);
  });

  it("Escape cierra y el botón Cerrar (X) tiene etiqueta", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    expect(screen.getByRole("button", { name: "Cerrar" })).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe("BulkInviteModal — contexto incompleto", () => {
  it("sin cliente (destino sin elegir) no se puede descargar ni cargar", () => {
    renderModal({ client: null });

    expect(screen.getByRole("button", { name: /descargar plantilla/i })).toBeDisabled();
    expect(screen.getByLabelText("Archivo Excel (.xlsx)")).toBeDisabled();
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeDisabled();
    expect(screen.getByTestId("bulk-invite-needs-context")).toBeInTheDocument();
  });

  it("al cambiar el contexto se descarta el archivo y el resumen", async () => {
    const user = userEvent.setup();
    const client = makeClient();
    const { rerender } = renderModal({ client });
    await pick(user, xlsx("antes.xlsx"));
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    expect(await screen.findByTestId("bulk-invite-preview")).toBeInTheDocument();

    rerender(<BulkInviteModal client={client} contextKey="ctx-2" onClose={vi.fn()} />);

    expect(screen.queryByTestId("bulk-invite-preview")).not.toBeInTheDocument();
    expect(screen.queryByText(/antes\.xlsx/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeDisabled();
  });
});

describe("BulkInviteModal — plantilla", () => {
  it("descarga la plantilla con el cliente del contexto", async () => {
    const user = userEvent.setup();
    const { client } = renderModal();

    await user.click(screen.getByRole("button", { name: /descargar plantilla/i }));

    expect(client.downloadTemplate).toHaveBeenCalledTimes(1);
  });

  it("un 403 al descargar se muestra sin romper el modal", async () => {
    const user = userEvent.setup();
    renderModal({ client: makeClient({ downloadTemplate: vi.fn().mockRejectedValue(new ApiError(403, "x", null)) }) });

    await user.click(screen.getByRole("button", { name: /descargar plantilla/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
});

describe("BulkInviteModal — validación local del archivo (AC2)", () => {
  it("un archivo que no es .xlsx se rechaza sin llamar al servidor", async () => {
    const user = userEvent.setup({ applyAccept: false });
    const { client } = renderModal();

    await pick(user, new File(["a,b"], "usuarios.csv"));

    expect(await screen.findByRole("alert")).toHaveTextContent(/\.xlsx/);
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeDisabled();
    expect(client.submit).not.toHaveBeenCalled();
  });

  it("un archivo de más de 1 MB se rechaza sin llamar al servidor", async () => {
    const user = userEvent.setup();
    const { client } = renderModal();

    await pick(user, new File([new Uint8Array(1024 * 1024 + 1)], "grande.xlsx"));

    expect(await screen.findByRole("alert")).toHaveTextContent(/1 MB/);
    expect(client.submit).not.toHaveBeenCalled();
  });
});

describe("BulkInviteModal — flujo completo (AC1)", () => {
  it("valida (dryRun), muestra el resumen, confirma (commit) y muestra el resultado", async () => {
    const user = userEvent.setup();
    const { client, onCompleted } = renderModal();

    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    const preview = await screen.findByTestId("bulk-invite-preview");
    expect(client.submit).toHaveBeenLastCalledWith(expect.any(File), expect.objectContaining({ dryRun: true }));
    expect(within(preview).getByText("Listas para invitar").nextElementSibling).toHaveTextContent("2");
    expect(within(preview).getByText(/Aún no se creó nada/)).toBeInTheDocument();
    expect(onCompleted).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: /confirmar e invitar \(2\)/i }));

    const result = await screen.findByTestId("bulk-invite-result");
    expect(client.submit).toHaveBeenLastCalledWith(expect.any(File), expect.objectContaining({ dryRun: false }));
    expect(within(result).getByText("Invitaciones creadas").nextElementSibling).toHaveTextContent("2");
    expect(within(result).getByText("Rechazadas").nextElementSibling).toHaveTextContent("1");
    expect(onCompleted).toHaveBeenCalledWith(commitReport);
    expect(screen.getByTestId("bulk-invite-live")).toHaveTextContent(/2 invitaciones creadas/);
  });

  it("lista las filas con error (fila, correo, motivo) y descarga un CSV con fórmulas escapadas", async () => {
    const user = userEvent.setup();
    renderModal();
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar e invitar/i }));
    const result = await screen.findByTestId("bulk-invite-result");

    const list = within(result).getByRole("list", { name: /filas con error/i });
    expect(within(list).getByText("Fila 4")).toBeInTheDocument();
    expect(within(list).getByText("=cmd@x.co")).toBeInTheDocument();
    expect(within(list).getByText(EMAIL_ALREADY_ASSOCIATED_MESSAGE)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /descargar csv de errores/i }));

    expect(downloadMock).toHaveBeenCalledTimes(1);
    const [content, name, mime] = downloadMock.mock.calls[0];
    expect(name).toMatch(/^errores-invitacion-masiva-\d{4}-\d{2}-\d{2}\.csv$/);
    expect(mime).toMatch(/text\/csv/);
    expect(content).toContain(`"'=cmd@x.co"`);
    expect(content).not.toContain("ana@x.co"); // solo las filas que fallaron
  });

  it("sin filas con error no ofrece el CSV", async () => {
    const user = userEvent.setup();
    const clean: BulkInvitationReport = { ...commitReport, rejected: 0, rows: commitReport.rows.slice(0, 2), total: 2 };
    renderModal({
      client: makeClient({
        submit: vi.fn(async (_f: File, o: { dryRun: boolean }) => (o.dryRun ? { ...dryReport, rejected: 0, total: 2, rows: dryReport.rows.slice(0, 2) } : clean)),
      }),
    });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar e invitar/i }));

    await screen.findByTestId("bulk-invite-result");
    expect(screen.queryByRole("button", { name: /csv de errores/i })).not.toBeInTheDocument();
  });

  it("«Elegir otro archivo» vuelve al estado vacío sin crear nada", async () => {
    const user = userEvent.setup();
    const { client } = renderModal();
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    await user.click(await screen.findByRole("button", { name: /elegir otro archivo/i }));

    expect(screen.queryByTestId("bulk-invite-preview")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeDisabled();
    expect(client.submit).toHaveBeenCalledTimes(1); // solo el dryRun
  });

  it("si el dryRun no deja ninguna fila válida no se puede confirmar", async () => {
    const user = userEvent.setup();
    renderModal({
      client: makeClient({
        submit: vi.fn().mockResolvedValue({ ...dryReport, valid: 0, rejected: 3, rows: dryReport.rows.map((r) => ({ ...r, status: "rejected" as const, errorCode: "ROLE_NOT_FOUND" })) }),
      }),
    });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    expect(await screen.findByText(/ninguna fila se puede invitar/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirmar e invitar/i })).toBeDisabled();
  });
});

describe("BulkInviteModal — estado cargando (AC3)", () => {
  it("durante la validación y el commit deshabilita controles, marca aria-busy y no se puede cerrar", async () => {
    const user = userEvent.setup();
    let resolveDry!: (r: BulkInvitationReport) => void;
    let resolveCommit!: (r: BulkInvitationReport) => void;
    const submit = vi
      .fn()
      .mockImplementationOnce(() => new Promise<BulkInvitationReport>((r) => (resolveDry = r)))
      .mockImplementationOnce(() => new Promise<BulkInvitationReport>((r) => (resolveCommit = r)));
    const { onClose } = renderModal({ client: makeClient({ submit }) });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-busy", "true");
    expect(screen.getByTestId("bulk-invite-loading")).toHaveTextContent(/validando/i);
    expect(screen.getByRole("button", { name: /validando/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /descargar plantilla/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cerrar" })).toBeDisabled();
    expect(screen.getByLabelText("Archivo Excel (.xlsx)")).toBeDisabled();
    await user.keyboard("{Escape}");
    expect(onClose).not.toHaveBeenCalled();

    await act(async () => resolveDry(dryReport));
    await user.click(await screen.findByRole("button", { name: /confirmar e invitar/i }));

    expect(screen.getByTestId("bulk-invite-loading")).toHaveTextContent(/no cierres esta ventana/i);
    expect(screen.getByRole("button", { name: /invitando/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /elegir otro archivo/i })).toBeDisabled();

    await act(async () => resolveCommit(commitReport));
    expect(await screen.findByTestId("bulk-invite-result")).toBeInTheDocument();
    expect(dialog).toHaveAttribute("aria-busy", "false");
  });

  it("un doble clic en Confirmar envía un solo commit", async () => {
    const user = userEvent.setup();
    let resolveCommit!: (r: BulkInvitationReport) => void;
    const submit = vi
      .fn()
      .mockResolvedValueOnce(dryReport)
      .mockImplementationOnce(() => new Promise<BulkInvitationReport>((r) => (resolveCommit = r)));
    renderModal({ client: makeClient({ submit }) });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    const confirm = await screen.findByRole("button", { name: /confirmar e invitar/i });

    await user.dblClick(confirm);

    expect(submit).toHaveBeenCalledTimes(2); // dryRun + 1 commit
    await act(async () => resolveCommit(commitReport));
  });
});

describe("BulkInviteModal — estado de error", () => {
  it("rechazo del archivo completo (código estable): mensaje claro y el archivo se puede reintentar", async () => {
    const user = userEvent.setup();
    const submit = vi.fn().mockRejectedValue(new ApiError(400, "x", { error: "too_many_rows" }));
    renderModal({ client: makeClient({ submit }) });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/supera las 50 filas/i);
    expect(screen.getByRole("button", { name: /validar archivo/i })).toBeEnabled();
  });

  it("403 del backend en la validación: mensaje de permiso sin romper la pantalla (HU #14428 AC2)", async () => {
    const user = userEvent.setup();
    renderModal({ client: makeClient({ submit: vi.fn().mockRejectedValue(new ApiError(403, "x", null)) }) });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("red caída en el dryRun: no hay riesgo, se puede reintentar", async () => {
    const user = userEvent.setup();
    const { onCompleted } = renderModal({
      client: makeClient({ submit: vi.fn().mockRejectedValue(new BulkInvitationsNetworkError("network")) }),
    });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));

    await screen.findByRole("alert");
    expect(screen.queryByTestId("bulk-invite-uncertain")).not.toBeInTheDocument();
    expect(onCompleted).not.toHaveBeenCalled();
  });

  it.each([
    ["red caída", new BulkInvitationsNetworkError("network")],
    ["timeout", new BulkInvitationsNetworkError("timeout")],
    ["504 del proxy", new ApiError(504, "x", null)],
  ])("%s en el commit: avisa que algunas invitaciones pueden haberse creado y refresca el listado", async (_n, failure) => {
    const user = userEvent.setup();
    const submit = vi.fn().mockResolvedValueOnce(dryReport).mockRejectedValueOnce(failure);
    const { onCompleted } = renderModal({ client: makeClient({ submit }) });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar e invitar/i }));

    expect(await screen.findByTestId("bulk-invite-uncertain")).toHaveTextContent(/pueden haberse creado/i);
    expect(screen.queryByRole("button", { name: /confirmar e invitar/i })).not.toBeInTheDocument();
    expect(onCompleted).toHaveBeenCalledWith(null);
  });

  it("un rechazo (4xx) en el commit vuelve al resumen sin marcarlo como incierto", async () => {
    const user = userEvent.setup();
    const submit = vi.fn().mockResolvedValueOnce(dryReport).mockRejectedValueOnce(new ApiError(403, "x", null));
    const { onCompleted } = renderModal({ client: makeClient({ submit }) });
    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    await user.click(await screen.findByRole("button", { name: /confirmar e invitar/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/no tienes permiso/i);
    expect(screen.queryByTestId("bulk-invite-uncertain")).not.toBeInTheDocument();
    expect(onCompleted).not.toHaveBeenCalled();
  });
});

describe("BulkInviteModal — slot de contexto", () => {
  it("entrega `disabled` al selector del llamador mientras hay una petición en curso", async () => {
    const user = userEvent.setup();
    let resolveDry!: (r: BulkInvitationReport) => void;
    const submit = vi.fn().mockImplementation(() => new Promise<BulkInvitationReport>((r) => (resolveDry = r)));
    renderModal({
      client: makeClient({ submit }),
      contextSlot: ({ disabled }) => <button type="button" disabled={disabled}>Selector</button>,
    });
    expect(screen.getByRole("button", { name: "Selector" })).toBeEnabled();

    await pick(user, xlsx());
    await user.click(screen.getByRole("button", { name: /validar archivo/i }));
    expect(screen.getByRole("button", { name: "Selector" })).toBeDisabled();

    await act(async () => resolveDry(dryReport));
    await waitFor(() => expect(screen.getByRole("button", { name: "Selector" })).toBeEnabled());
  });
});
