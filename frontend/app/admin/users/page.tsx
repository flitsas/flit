"use client";

// Gestión de usuarios para administración (HU #10174). Ruta bajo /admin/* protegida
// por el middleware (rol SuperAdmin). Permite restablecer contraseña y aplicar
// bloqueo temporal por usuario. (La fuente del listado es un endpoint pendiente;
// se usa un conjunto de ejemplo para la UI.)
import { useState } from "react";
import { BlockUserModal } from "@/components/admin/users/BlockUserModal";
import { ResetPasswordModal } from "@/components/admin/users/ResetPasswordModal";

interface UserRow {
  email: string;
  displayName: string;
}

const DEMO_USERS: UserRow[] = [
  { email: "demo@flit.local", displayName: "Usuario Demo" },
  { email: "operador@flit.local", displayName: "Operador" },
];

export default function AdminUsersPage() {
  const [resetTarget, setResetTarget] = useState<string | null>(null);
  const [blockTarget, setBlockTarget] = useState<string | null>(null);

  return (
    <main className="min-h-screen bg-[#eef5ff] p-8">
      <div className="max-w-3xl mx-auto">
        <h1 className="text-2xl font-bold text-[#162744] mb-6">Gestión de usuarios</h1>
        <div className="bg-white rounded-2xl shadow-xl overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-slate-50 text-left text-slate-500">
                <th className="px-4 py-3">Usuario</th>
                <th className="px-4 py-3">Correo</th>
                <th className="px-4 py-3 text-right">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {DEMO_USERS.map((user) => (
                <tr key={user.email} className="border-t border-slate-100">
                  <td className="px-4 py-3 text-[#162744]">{user.displayName}</td>
                  <td className="px-4 py-3 text-slate-600">{user.email}</td>
                  <td className="px-4 py-3 text-right space-x-2">
                    <button
                      type="button"
                      onClick={() => setResetTarget(user.email)}
                      className="rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
                      style={{ background: "#557eff" }}
                    >
                      Restablecer
                    </button>
                    <button
                      type="button"
                      onClick={() => setBlockTarget(user.email)}
                      className="rounded-lg px-3 py-1.5 text-xs font-semibold text-white"
                      style={{ background: "#ff4e00" }}
                    >
                      Bloquear
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {resetTarget && <ResetPasswordModal email={resetTarget} onClose={() => setResetTarget(null)} />}
      {blockTarget && <BlockUserModal email={blockTarget} onClose={() => setBlockTarget(null)} />}
    </main>
  );
}
