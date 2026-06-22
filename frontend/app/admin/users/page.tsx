"use client";

import { useState } from "react";
import { BlockUserModal } from "@/components/admin/users/BlockUserModal";
import { ResetPasswordModal } from "@/components/admin/users/ResetPasswordModal";
import { InviteUserModal } from "@/components/admin/users/InviteUserModal";

interface UserRow {
  email: string;
  displayName: string;
}

interface InvitationRow {
  email: string;
  status: "pending";
}

const DEMO_USERS: UserRow[] = [
  { email: "demo@flit.local", displayName: "Usuario Demo" },
  { email: "operador@flit.local", displayName: "Operador" },
];

export default function AdminUsersPage() {
  const [resetTarget, setResetTarget] = useState<string | null>(null);
  const [blockTarget, setBlockTarget] = useState<string | null>(null);
  const [inviteOpen, setInviteOpen] = useState(false);
  const [invitations, setInvitations] = useState<InvitationRow[]>([]);

  function handleInviteSuccess(email: string) {
    setInvitations((prev) => [...prev, { email, status: "pending" }]);
  }

  return (
    <main className="min-h-screen bg-[#eef5ff] p-8">
      <div className="max-w-3xl mx-auto space-y-8">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold text-[#162744]">Gestión de usuarios</h1>
          <button
            type="button"
            onClick={() => setInviteOpen(true)}
            className="rounded-xl px-4 py-2.5 text-sm font-semibold text-white"
            style={{ background: "#557eff" }}
          >
            Invitar usuario
          </button>
        </div>

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

        {invitations.length > 0 && (
          <div>
            <h2 className="text-base font-semibold text-[#162744] mb-3">Invitaciones pendientes</h2>
            <div className="bg-white rounded-2xl shadow-xl overflow-hidden">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-slate-50 text-left text-slate-500">
                    <th className="px-4 py-3">Correo</th>
                    <th className="px-4 py-3">Estado</th>
                  </tr>
                </thead>
                <tbody>
                  {invitations.map((inv, i) => (
                    <tr key={`${inv.email}-${i}`} className="border-t border-slate-100">
                      <td className="px-4 py-3 text-slate-700">{inv.email}</td>
                      <td className="px-4 py-3">
                        <span className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                          Pendiente
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {resetTarget && <ResetPasswordModal email={resetTarget} onClose={() => setResetTarget(null)} />}
      {blockTarget && <BlockUserModal email={blockTarget} onClose={() => setBlockTarget(null)} />}
      {inviteOpen && (
        <InviteUserModal
          onClose={() => setInviteOpen(false)}
          onSuccess={handleInviteSuccess}
        />
      )}
    </main>
  );
}
