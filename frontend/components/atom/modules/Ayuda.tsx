import { LifeBuoy, MessageCircle, BookOpen, Mail } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";

const FAQ = [
  { q: "¿Cómo inicio un nuevo traspaso?", a: "Ve al módulo Trámites y presiona 'Nuevo Traspaso'. Sigue los 4 pasos del asistente." },
  { q: "¿Cómo invito a un colaborador?", a: "Desde Usuarios y Permisos, presiona 'Invitar Colaborador' y asigna el rol correspondiente." },
  { q: "¿Qué hago si el RUNT está caído?", a: "El sistema reintentará automáticamente. Recibirás una notificación cuando se restablezca." },
  { q: "¿Cómo descargo reportes históricos?", a: "En el módulo Reportes encuentras todos los reportes disponibles en PDF." },
];

export function Ayuda() {
  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle title="Centro de Ayuda" subtitle="Soporte 24/7, documentación y guías de operación." />
      <div className="grid grid-cols-4 gap-3 shrink-0">
        {[
          { l: "Chat en vivo", d: "Respuesta < 2 min", i: MessageCircle },
          { l: "Base de conocimiento", d: "+120 artículos", i: BookOpen },
          { l: "Email soporte", d: "soporte@flit.io", i: Mail },
          { l: "Soporte prioritario", d: "Plan Enterprise", i: LifeBuoy },
        ].map((k) => {
          const Icon = k.i;
          return (
            <div key={k.l} className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border">
              <div className="h-10 w-10 rounded-xl grid place-items-center mb-2" style={{ background: "rgba(0,219,213,0.15)" }}>
                <Icon className="h-5 w-5" style={{ color: "#00DBD5" }} />
              </div>
              <p className="text-xs font-bold">{k.l}</p>
              <p className="text-[10px] opacity-60 mt-0.5">{k.d}</p>
            </div>
          );
        })}
      </div>
      <div className="flex-1 min-h-0 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border overflow-y-auto">
        <h2 className="text-sm font-bold mb-3">Preguntas frecuentes</h2>
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
