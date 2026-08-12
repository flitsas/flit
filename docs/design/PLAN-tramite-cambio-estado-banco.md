# Plan local — Trámite Aprobado / Rechazado (banco de pruebas)

> Generado: 2026-08-11 · Actualizado: 2026-08-11 · Caso C con registro ADO diferido  
> Disparador productivo: **fuera de alcance** (fase posterior)

## Objetivo

Agregar al banco de notificaciones las plantillas **Trámite Aprobado** y **Trámite Rechazado** con formatos FLIT / Renting y acciones de prueba.

## Diseño técnico (resumen)

```
Catálogo (+2)
  id: tramites.aprobado  / tramites.rechazado
  module: Tramites
  trigger: ProcedureStatusChanged (declarativo; sin handler productivo aún)

Composer (static compartido)
  ComposeFlit(sample, assetsBaseUrl)  → HTML marca FLIT + <img> HTTPS
  ComposeRenting(sample, assetsBaseUrl) → HTML diseño Renting (header progreso + footer contacto)

Preview GET .../plantillas/{id}/muestra?channel=FLIT_SMTP|TENANT_API
Send   POST .../buzon-pruebas/envios { templateId, channel }
```

## UX

| Acción | Canal | Remitente |
|--------|-------|-----------|
| Preview FLIT | — (solo render) | n/a |
| Enviar FLIT | `FLIT_SMTP` | `Smtp:DefaultSenderEmail` (prod: tramitesvehiculos@flitsas.com) |
| Preview Renting | — | n/a |
| Enviar Renting | `TENANT_API` | Remitente Renting configurado |

Las demás plantillas conservan «Ver en vivo» / «Enviar prueba» según el selector de canal.

## Assets

Copiar banner/logo FLIT y header/footer Renting a `frontend/public/email-assets/`.  
Base URL configurable: `Notifications:EmailAssets:BaseUrl` (absoluta, para clientes de correo).

## Fases de implementación

1. Backend: enums, catálogo, composer, preview, send render por canal, tests
2. Frontend: API `channel` en muestra, modal, 4 botones, assets públicos, tests
3. Al abrir PR: `/register-work` en ADO
