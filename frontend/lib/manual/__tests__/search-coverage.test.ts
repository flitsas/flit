import { describe, expect, it } from "vitest";

import { MANUAL_ARTICLES, searchManualArticles, visibleAudiences } from "@/lib/manual/catalog";
import type { ManualProfile } from "@/lib/manual/types";

/**
 * HU-H — «toda pregunta de referencia devuelve el artículo esperado». Es la red de seguridad de
 * las `keywords`: si alguien edita un artículo y pierde la palabra clave, este test lo dice.
 * Cada fila: la pregunta como la escribiría un usuario, el perfil que pregunta y el slug que debe
 * salir entre los TRES primeros (no necesariamente el primero: varios artículos pueden aplicar).
 */
const CASOS: { pregunta: string; perfil: ManualProfile; slug: string }[] = [
  // Gestor
  { pregunta: "cómo creo un trámite", perfil: "gestor", slug: "1-gestor/2-crear-tramite" },
  { pregunta: "términos y condiciones al crear", perfil: "gestor", slug: "1-gestor/2-crear-tramite" },
  { pregunta: "carga masiva excel", perfil: "gestor", slug: "1-gestor/2-crear-tramite" },
  { pregunta: "qué documentos necesito para matrícula", perfil: "gestor", slug: "1-gestor/3-documentos-tramite" },
  { pregunta: "cómo envío prevalidación", perfil: "gestor", slug: "1-gestor/4-prevalidacion" },
  { pregunta: "buscar trámite por radicado", perfil: "gestor", slug: "1-gestor/5-seguimiento" },
  { pregunta: "qué significa entregado", perfil: "gestor", slug: "1-gestor/5-seguimiento" },
  { pregunta: "exportar trámites a excel", perfil: "gestor", slug: "1-gestor/5-seguimiento" },
  { pregunta: "filtros y consultas del listado", perfil: "gestor", slug: "1-gestor/5-seguimiento" },
  { pregunta: "ruta corta ruta larga matrícula", perfil: "gestor", slug: "1-gestor/7-ruta-placa" },
  { pregunta: "qué significa asignado", perfil: "gestor", slug: "1-gestor/7-ruta-placa" },
  { pregunta: "dígito de preferencia de placa", perfil: "gestor", slug: "1-gestor/7-ruta-placa" },
  { pregunta: "enviar al ot soat impuestos", perfil: "gestor", slug: "1-gestor/7-ruta-placa" },
  { pregunta: "validación de identidad biométrica", perfil: "gestor", slug: "1-gestor/8-identidad" },
  { pregunta: "validaciones atascadas", perfil: "gestor", slug: "1-gestor/8-identidad" },
  { pregunta: "vigencia de la identidad", perfil: "gestor", slug: "1-gestor/8-identidad" },
  { pregunta: "historial por placa", perfil: "gestor", slug: "1-gestor/9-historial-placa" },
  { pregunta: "qué le ha pasado a esta placa", perfil: "gestor", slug: "1-gestor/9-historial-placa" },
  { pregunta: "cómo solicito una revocatoria", perfil: "gestor", slug: "1-gestor/10-revocatorias" },
  { pregunta: "ventana de revocatoria vencida", perfil: "gestor", slug: "1-gestor/10-revocatorias" },
  { pregunta: "reportes de productividad", perfil: "gestor", slug: "1-gestor/11-reportes" },
  { pregunta: "reportes detallados leasing", perfil: "gestor", slug: "1-gestor/11-reportes" },
  { pregunta: "consultas personalizadas", perfil: "gestor", slug: "1-gestor/11-reportes" },
  { pregunta: "invitar usuario", perfil: "gestor", slug: "1-gestor/12-usuarios" },
  { pregunta: "restablecer contraseña de un usuario", perfil: "gestor", slug: "1-gestor/12-usuarios" },
  { pregunta: "necesito ayuda dr flit", perfil: "gestor", slug: "1-gestor/6-ayuda-dr-flit" },
  // Transversales
  { pregunta: "formato de fecha y hora de colombia", perfil: "gestor", slug: "0-introduccion/4-fechas-y-horas" },
  { pregunta: "zona horaria", perfil: "ot_admin", slug: "0-introduccion/4-fechas-y-horas" },
  { pregunta: "cómo navegar el manual", perfil: "ot_admin", slug: "0-introduccion/2-como-navegar" },
  // OT
  { pregunta: "requisitos documentales", perfil: "ot_admin", slug: "2-ot/7-requisitos" },
  { pregunta: "asignar placa a un trámite", perfil: "ot_admin", slug: "2-ot/1-tramites-bandeja" },
  { pregunta: "liberar placa", perfil: "ot_admin", slug: "2-ot/1-tramites-bandeja" },
  { pregunta: "adjuntar licencia de tránsito al aprobar", perfil: "ot_admin", slug: "2-ot/1-tramites-bandeja" },
  { pregunta: "causales de rechazo", perfil: "ot_admin", slug: "2-ot/1-tramites-bandeja" },
  { pregunta: "decidir revocatoria", perfil: "ot_admin", slug: "2-ot/9-revocatorias" },
  { pregunta: "aprobar una solicitud de revocatoria", perfil: "ot_admin", slug: "2-ot/9-revocatorias" },
  { pregunta: "mandatario general del organismo", perfil: "ot_admin", slug: "2-ot/10-mandatos" },
  { pregunta: "contrato de mandato", perfil: "ot_admin", slug: "2-ot/10-mandatos" },
  { pregunta: "validar impronta", perfil: "ot_admin", slug: "2-ot/11-validar-impronta" },
  { pregunta: "verificar firma digital de la impronta", perfil: "ot_admin", slug: "2-ot/11-validar-impronta" },
  { pregunta: "ventana de revocatoria días hábiles", perfil: "ot_admin", slug: "2-ot/12-configuracion" },
  { pregunta: "consola en solo lectura quipux", perfil: "ot_admin", slug: "2-ot/12-configuracion" },
  { pregunta: "buscar radicado en dr flit", perfil: "ot_admin", slug: "2-ot/8-ayuda-dr-flit" },
  // Admin de Compañía
  { pregunta: "configuración de la empresa", perfil: "admin_company", slug: "3-admin-company/1-consola" },
  { pregunta: "lista blanca de correos", perfil: "admin_company", slug: "3-admin-company/1-consola" },
  { pregunta: "proveedores de consulta y avalúo", perfil: "admin_company", slug: "3-admin-company/1-consola" },
  { pregunta: "registrar representante legal", perfil: "admin_company", slug: "3-admin-company/2-representantes-mandatarios" },
  { pregunta: "firma del baúl del mandatario", perfil: "admin_company", slug: "3-admin-company/2-representantes-mandatarios" },
  { pregunta: "red de clientes concesión", perfil: "admin_company", slug: "3-admin-company/3-red-de-clientes" },
  { pregunta: "alcance toda la red solo consulta", perfil: "admin_company", slug: "3-admin-company/3-red-de-clientes" },
  { pregunta: "vincular cliente existente", perfil: "admin_company", slug: "3-admin-company/3-red-de-clientes" },
  { pregunta: "publicar identidad de marca logotipo colores", perfil: "admin_company", slug: "3-admin-company/4-marca-y-dominio" },
  { pregunta: "dominio propio registro txt dns", perfil: "admin_company", slug: "3-admin-company/4-marca-y-dominio" },
  { pregunta: "certificado rues sin trámite", perfil: "admin_company", slug: "3-admin-company/5-generacion-documental" },
  { pregunta: "documento de transferencia de dominio leasing", perfil: "admin_company", slug: "3-admin-company/5-generacion-documental" },
  // El Admin de Compañía también opera como Gestor
  { pregunta: "cómo solicito una revocatoria", perfil: "admin_company", slug: "1-gestor/10-revocatorias" },
  // Super Admin
  { pregunta: "crear compañía", perfil: "superadmin", slug: "4-superadmin/1-companias-y-organismos" },
  { pregunta: "activar organismo de tránsito código integrador", perfil: "superadmin", slug: "4-superadmin/1-companias-y-organismos" },
  { pregunta: "causales de rechazo catálogo", perfil: "superadmin", slug: "4-superadmin/1-companias-y-organismos" },
  { pregunta: "matriz resuelta overrides ot", perfil: "superadmin", slug: "4-superadmin/2-documental-e-improntas" },
  { pregunta: "generar certificado de improntas", perfil: "superadmin", slug: "4-superadmin/2-documental-e-improntas" },
  { pregunta: "tipos de trámite capacidades recorrido", perfil: "superadmin", slug: "4-superadmin/3-plataforma" },
  { pregunta: "plantillas de correo notificaciones", perfil: "superadmin", slug: "4-superadmin/3-plataforma" },
  { pregunta: "confirmación runt", perfil: "superadmin", slug: "4-superadmin/3-plataforma" },
  { pregunta: "integración quipux cadencia", perfil: "superadmin", slug: "4-superadmin/4-integraciones-y-procesos" },
  { pregunta: "trazabilidad ict", perfil: "superadmin", slug: "4-superadmin/4-integraciones-y-procesos" },
  { pregunta: "procesos periódicos jobs", perfil: "superadmin", slug: "4-superadmin/4-integraciones-y-procesos" },
  { pregunta: "migración del sistema anterior", perfil: "superadmin", slug: "4-superadmin/4-integraciones-y-procesos" },
  { pregunta: "roles y permisos rbac", perfil: "superadmin", slug: "4-superadmin/5-rbac-y-auditoria" },
  { pregunta: "auditoría rastro de seguridad", perfil: "superadmin", slug: "4-superadmin/5-rbac-y-auditoria" },
  { pregunta: "buscar en todas las compañías", perfil: "superadmin", slug: "4-superadmin/6-dr-flit-global" },
  // El Super Admin también ve la documentación de los demás perfiles (soporte)
  { pregunta: "liberar placa", perfil: "superadmin", slug: "2-ot/1-tramites-bandeja" },
];

describe("manual/search-coverage (HU-H)", () => {
  for (const c of CASOS) {
    it(`«${c.pregunta}» (${c.perfil}) → ${c.slug}`, () => {
      const hits = searchManualArticles(c.pregunta, 3, { audiences: visibleAudiences(c.perfil) });
      expect(hits.map((h) => h.slug), hits.map((h) => `${h.slug}:${h.score}`).join(", ")).toContain(
        c.slug,
      );
    });
  }

  it("un Organismo nunca recibe artículos del Gestor", () => {
    for (const c of CASOS.filter((x) => x.perfil === "ot_admin")) {
      const hits = searchManualArticles(c.pregunta, 8, { audiences: visibleAudiences("ot_admin") });
      expect(hits.every((h) => h.audience === "Todos" || h.audience === "Organismo de Tránsito")).toBe(
        true,
      );
    }
  });

  it("un Admin de Compañía nunca recibe artículos del Organismo ni de Super Admin", () => {
    for (const c of CASOS.filter((x) => x.perfil === "admin_company")) {
      const hits = searchManualArticles(c.pregunta, 8, { audiences: visibleAudiences("admin_company") });
      expect(
        hits.every((h) => h.audience === "Todos" || h.audience === "Gestor" || h.audience === "Admin de Compañía"),
      ).toBe(true);
    }
  });

  it("un Gestor nunca recibe artículos del Organismo ni de administración", () => {
    for (const c of CASOS.filter((x) => x.perfil === "gestor")) {
      const hits = searchManualArticles(c.pregunta, 8, { audiences: visibleAudiences("gestor") });
      expect(hits.every((h) => h.audience === "Todos" || h.audience === "Gestor")).toBe(true);
    }
  });

  it("ningún artículo dice ya que la administración o el Super Admin quedan fuera del manual", () => {
    const stale = /No incluye consolas exclusivas|quedan fuera|No\. Documenta Gestor y Organismo/i;
    for (const a of MANUAL_ARTICLES) {
      const text = [a.summary, ...a.blocks.flatMap((b) => [...b.paragraphs, ...(b.bullets ?? [])])].join(" ");
      expect(text, a.slug).not.toMatch(stale);
    }
  });
});
