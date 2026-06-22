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
});
