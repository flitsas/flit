import { describe, expect, it } from "vitest";

import {
  getArticleBySlug,
  MANUAL_ARTICLES,
  resolveContextArticle,
  searchManualArticles,
  visibleAudiences,
} from "@/lib/manual/catalog";
import {
  MANUAL_MODULE_ARTICLES,
  MANUAL_OT_TAB_ARTICLES,
  MANUAL_PATH_ARTICLES,
  MANUAL_PATH_SUFFIX_ARTICLES,
} from "@/lib/manual/articles/meta";

describe("manual/audience (HU-F)", () => {
  it("cada perfil ve lo común más lo suyo; el OT no ve el flujo del Gestor", () => {
    expect(visibleAudiences("gestor")).toEqual(["Todos", "Gestor"]);
    expect(visibleAudiences("ot_admin")).toEqual(["Todos", "Organismo de Tránsito"]);
    expect(visibleAudiences("admin_company")).toContain("Admin de Compañía");
    expect(visibleAudiences("admin_company")).toContain("Gestor");
    expect(visibleAudiences("superadmin")).toContain("Super Admin");
    expect(visibleAudiences("superadmin")).toContain("Organismo de Tránsito");
  });

  it("la búsqueda filtra por audiencia: un Gestor no recibe la preasignación del OT", () => {
    const sinFiltro = searchManualArticles("preasignacion de placas");
    expect(sinFiltro.some((h) => h.slug === "2-ot/2-preasignacion")).toBe(true);

    const gestor = searchManualArticles("preasignacion de placas", 8, {
      audiences: visibleAudiences("gestor"),
    });
    expect(gestor.some((h) => h.audience === "Organismo de Tránsito")).toBe(false);

    const ot = searchManualArticles("preasignacion de placas", 8, {
      audiences: visibleAudiences("ot_admin"),
    });
    expect(ot.some((h) => h.slug === "2-ot/2-preasignacion")).toBe(true);
    expect(ot.some((h) => h.audience === "Gestor")).toBe(false);
  });

  it("lo común («Todos») lo ve cualquier perfil", () => {
    for (const profile of ["gestor", "ot_admin", "admin_company", "superadmin"] as const) {
      const hits = searchManualArticles("como navegar el manual", 8, {
        audiences: visibleAudiences(profile),
      });
      expect(hits.some((h) => h.slug === "0-introduccion/2-como-navegar")).toBe(true);
    }
  });
});

describe("manual/context (HU-G)", () => {
  it("todo slug del mapa módulo→artículo existe en el catálogo", () => {
    for (const slug of [
      ...Object.values(MANUAL_MODULE_ARTICLES),
      ...Object.values(MANUAL_OT_TAB_ARTICLES),
    ]) {
      expect(getArticleBySlug(slug), slug).toBeDefined();
    }
    for (const { slug } of [...MANUAL_PATH_ARTICLES, ...MANUAL_PATH_SUFFIX_ARTICLES]) {
      expect(getArticleBySlug(slug), slug).toBeDefined();
    }
    expect(MANUAL_ARTICLES.length).toBeGreaterThan(0);
  });

  it("módulo de la SPA → artículo del Gestor", () => {
    expect(resolveContextArticle("/|tramites")?.slug).toBe("1-gestor/5-seguimiento");
    expect(resolveContextArticle("/|dashboard")?.slug).toBe("1-gestor/1-inicio");
  });

  it("ruta de página gana sobre el módulo: /tramites/nuevo → crear trámite", () => {
    expect(resolveContextArticle("/tramites/nuevo|tramites")?.slug).toBe(
      "1-gestor/2-crear-tramite",
    );
    expect(resolveContextArticle("/tramites/abc-123|dashboard")?.slug).toBe(
      "1-gestor/5-seguimiento",
    );
    // E2E SA-18: el listado vive en `/tramites` sin barra final.
    expect(resolveContextArticle("/tramites|tramites")?.slug).toBe("1-gestor/5-seguimiento");
  });

  it("pestaña del hub OT por segmento de URL, aunque el módulo de la SPA diga dashboard", () => {
    expect(
      resolveContextArticle("/admin/transit-offices/ot-1/plate-ranges|dashboard")?.slug,
    ).toBe("2-ot/2-preasignacion");
    expect(
      resolveContextArticle("/admin/transit-offices/ot-1/client-procedures|dashboard")?.slug,
    ).toBe("2-ot/1-tramites-bandeja");
  });

  it("SPA y hub OT no se pisan: `usuarios`/`reportes` resuelven a artículos distintos", () => {
    expect(resolveContextArticle("/|usuarios")?.slug).toBe("1-gestor/12-usuarios");
    expect(resolveContextArticle("/admin/transit-offices/ot-1/usuarios|dashboard")?.slug).toBe(
      "2-ot/4-usuarios",
    );
    expect(resolveContextArticle("/|validaciones")?.slug).toBe("1-gestor/8-identidad");
    expect(resolveContextArticle("/|historial-placa")?.slug).toBe("1-gestor/9-historial-placa");
    expect(resolveContextArticle("/tramites/revocatorias|tramites")?.slug).toBe(
      "1-gestor/10-revocatorias",
    );
  });

  it("AdminCompany: ficha de compañía → consola; /children → red; generación documental por ruta", () => {
    expect(resolveContextArticle("/admin/companies/t-1|dashboard")?.slug).toBe(
      "3-admin-company/1-consola",
    );
    expect(resolveContextArticle("/admin/companies/t-1/children|dashboard")?.slug).toBe(
      "3-admin-company/3-red-de-clientes",
    );
    expect(resolveContextArticle("/admin/generacion-documental|dashboard")?.slug).toBe(
      "3-admin-company/5-generacion-documental",
    );
    // Un Gestor sin rol admin no recibe la consola aunque llegue a la URL.
    expect(
      resolveContextArticle("/admin/companies/t-1|dashboard", visibleAudiences("gestor")),
    ).toBeNull();
    expect(
      resolveContextArticle("/admin/companies/t-1|dashboard", visibleAudiences("admin_company"))?.slug,
    ).toBe("3-admin-company/1-consola");
  });

  it("sin artículo para el lugar → null; audiencia no visible → null", () => {
    expect(resolveContextArticle("/|modulo-inexistente")).toBeNull();
    expect(resolveContextArticle("/admin/companies|dashboard")?.slug).toBe(
      "4-superadmin/1-companias-y-organismos",
    );
    expect(resolveContextArticle("/admin/companies|dashboard", visibleAudiences("gestor"))).toBeNull();
    expect(resolveContextArticle("/admin/rbac|dashboard")?.slug).toBe("4-superadmin/5-rbac-y-auditoria");
    expect(resolveContextArticle("/|auditoria")?.slug).toBe("4-superadmin/5-rbac-y-auditoria");
    expect(resolveContextArticle("/|log-qx")?.slug).toBe("4-superadmin/4-integraciones-y-procesos");
    expect(resolveContextArticle("/admin/transit-offices|dashboard")?.slug).toBe(
      "4-superadmin/1-companias-y-organismos",
    );
    expect(resolveContextArticle("/|profile-x")).toBeNull();
    expect(resolveContextArticle(null)).toBeNull();
    expect(
      resolveContextArticle("/admin/transit-offices/ot-1/rules|dashboard", visibleAudiences("gestor")),
    ).toBeNull();
    expect(
      resolveContextArticle("/admin/transit-offices/ot-1/rules|dashboard", visibleAudiences("ot_admin"))
        ?.slug,
    ).toBe("2-ot/5-reglas");
  });
});
