import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { loginUser } from "@/lib/api/auth";
import { storeToken } from "@/lib/auth/session";
import { Login } from "../Login";

vi.mock("@/lib/api/auth", () => ({ loginUser: vi.fn() }));
vi.mock("@/lib/auth/session", () => ({
  storeToken: vi.fn(),
  rememberEmail: vi.fn(),
}));

const loginMock = vi.mocked(loginUser);
const storeTokenMock = vi.mocked(storeToken);

function fillCreds() {
  fireEvent.change(screen.getByLabelText(/usuario corporativo/i), { target: { value: "admin@flit.io" } });
  fireEvent.change(screen.getByLabelText(/contraseña/i), { target: { value: "Secret123" } });
}

describe("Login (Feature #10113 — login real)", () => {
  beforeEach(() => vi.clearAllMocks());

  it("AC1 — credenciales válidas → almacena JWT y notifica autenticación", async () => {
    loginMock.mockResolvedValue({ accessToken: "jwt.abc.def", expiresInSeconds: 43200, tokenType: "Bearer" });
    const onAuthenticated = vi.fn();
    render(<Login onAuthenticated={onAuthenticated} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(onAuthenticated).toHaveBeenCalledOnce());
    expect(loginMock).toHaveBeenCalledWith("admin@flit.io", "Secret123");
    expect(storeTokenMock).toHaveBeenCalledWith("jwt.abc.def");
  });

  it("credenciales inválidas (401) → error accesible, sin autenticar", async () => {
    loginMock.mockRejectedValue(Object.assign(new Error("401"), { status: 401 }));
    const onAuthenticated = vi.fn();
    render(<Login onAuthenticated={onAuthenticated} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/incorrectos/i));
    expect(onAuthenticated).not.toHaveBeenCalled();
  });

  it("cuenta bloqueada (403) → panel de acceso restringido", async () => {
    loginMock.mockRejectedValue(Object.assign(new Error("403"), { status: 403 }));
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/acceso restringido/i));
  });

  // HU #10511 AC1 — todos los roles del usuario están inactivos.
  it("todos los roles desactivados (403 ALL_ROLES_INACTIVE) → mensaje específico de rol desactivado", async () => {
    loginMock.mockRejectedValue(
      Object.assign(new Error("403"), { status: 403, body: { code: "ALL_ROLES_INACTIVE" } }),
    );
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() =>
      expect(screen.getByRole("alert")).toHaveTextContent(/rol ha sido desactivado/i),
    );
  });

  // HU #10511 AC2 — el bloqueo temporal (sin ese code) no debe mostrar el mensaje de rol desactivado.
  it("cuenta bloqueada temporalmente sin ALL_ROLES_INACTIVE → sigue mostrando el mensaje de bloqueo, no el de rol", async () => {
    loginMock.mockRejectedValue(Object.assign(new Error("403"), { status: 403, body: { code: "ACCOUNT_TEMPORARILY_BLOCKED" } }));
    render(<Login onAuthenticated={vi.fn()} />);

    fillCreds();
    fireEvent.click(screen.getByRole("button", { name: /iniciar sesión/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent(/acceso restringido/i));
    expect(screen.queryByText(/rol ha sido desactivado/i)).not.toBeInTheDocument();
  });
});
