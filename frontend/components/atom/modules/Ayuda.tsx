import Link from "next/link";
import { LifeBuoy, MessageCircle, BookOpen, Mail } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";

const FAQ = [
  {
    q: "¿Cómo inicio un nuevo trámite?",
    a: "Ve a Trámites, elige el tipo disponible para tu compañía y sigue el asistente. Detalle en el Centro de Ayuda.",
  },
  {
    q: "¿Qué documentos necesito?",
    a: "Dependen del tipo de trámite y del OT. El wizard lista los adjuntos obligatorios; también consulta el manual.",
  },
  {
    q: "¿Cómo uso DR. FLIT para ayuda?",
    a: "Abre el chat, elige «Necesito ayuda» y describe tu duda. Si hay artículo, te lleva al manual.",
  },
  {
    q: "¿El manual es para administradores?",
    a: "No. Documenta Gestor y Organismo de Tránsito. Las consolas de SuperAdmin/AdminCompany quedan fuera.",
  },
];

export function Ayuda() {
  return (
    <div className="app-bg min-h-screen px-6 pt-6 pb-10 flex flex-col gap-4 text-[#162744] dark:text-white">
      <ModuleTitle
        title="Centro de Ayuda"
        subtitle="Documentación operativa, chat DR. FLIT y canales de soporte."
      />
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 shrink-0">
        {[
          {
            l: "Manual FLIT",
            d: "Docs Gestor y OT",
            i: BookOpen,
            href: "/manual",
          },
          { l: "Chat DR. FLIT", d: "«Necesito ayuda»", i: MessageCircle },
          { l: "Email soporte", d: "soporte@flitsas.com", i: Mail },
          { l: "Soporte prioritario", d: "Plan Enterprise", i: LifeBuoy },
        ].map((k) => {
          const Icon = k.i;
          const card = (
            <div className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border h-full">
              <div
                className="h-10 w-10 rounded-xl grid place-items-center mb-2"
                style={{ background: "rgba(0,219,213,0.15)" }}
              >
                <Icon className="h-5 w-5" style={{ color: "#00DBD5" }} />
              </div>
              <p className="text-xs font-bold">{k.l}</p>
              <p className="text-[10px] opacity-60 mt-0.5">{k.d}</p>
            </div>
          );
          return k.href ? (
            <Link key={k.l} href={k.href} className="block hover:opacity-90">
              {card}
            </Link>
          ) : (
            <div key={k.l}>{card}</div>
          );
        })}
      </div>
      <div className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border">
        <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
          <h2 className="text-sm font-bold">Preguntas frecuentes</h2>
          <Link
            href="/manual"
            className="text-xs font-semibold text-[#0F766E] hover:underline"
          >
            Abrir Centro de Ayuda →
          </Link>
        </div>
        <div className="space-y-3">
          {FAQ.map((f) => (
            <details key={f.q} className="p-3 rounded-xl border">
              <summary className="text-xs font-semibold cursor-pointer">{f.q}</summary>
              <p className="text-xs opacity-70 mt-2">{f.a}</p>
            </details>
          ))}
        </div>
      </div>
    </div>
  );
}
