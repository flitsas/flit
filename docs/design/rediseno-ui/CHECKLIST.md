# Checklist de cobertura

Generado desde el código el 2026-11-09. Cada fila se marca en las tres columnas: claro (C), oscuro (O) y celular 375 (M).
Marca `x` cuando la pantalla se vio, el árbol de accesibilidad coincide con la línea base y axe pasó.

## frontend: rutas

| Ruta | C | O | M |
|---|---|---|---|
| `/` | | | |
| `/403` | | | |
| `/admin/banners` | | | |
| `/admin/causales-rechazo` | | | |
| `/admin/companies` | | | |
| `/admin/companies/[tenantId]` | | | |
| `/admin/companies/[tenantId]/children` | | | |
| `/admin/documents` | | | |
| `/admin/documents/procedures` | | | |
| `/admin/documents/procedures/[procedureTypeId]` | | | |
| `/admin/generacion-documental` | | | |
| `/admin/generacion-documental/historial` | | | |
| `/admin/generacion-documental/lotes` | | | |
| `/admin/generacion-documental/lotes/[batchId]` | | | |
| `/admin/generacion-documental/transferencia` | | | |
| `/admin/improntas` | | | |
| `/admin/improntas/historial` | | | |
| `/admin/jobs` | | | |
| `/admin/jobs/ict` | | | |
| `/admin/migracion` | | | |
| `/admin/plataforma/configuracion-ia` | | | |
| `/admin/plataforma/confirmacion-runt` | | | |
| `/admin/plataforma/confirmacion-runt/configuracion` | | | |
| `/admin/plataforma/confirmacion-runt/historial` | | | |
| `/admin/plataforma/descarga-masiva` | | | |
| `/admin/plataforma/fur` | | | |
| `/admin/plataforma/mandatos` | | | |
| `/admin/plataforma/mensajes-muertos` | | | |
| `/admin/plataforma/notificaciones` | | | |
| `/admin/plataforma/persona-natural` | | | |
| `/admin/plataforma/tipos-tramite` | | | |
| `/admin/quipux` | | | |
| `/admin/rbac` | | | |
| `/admin/transit-offices` | | | |
| `/admin/transit-offices/[id]` | | | |
| `/admin/transit-offices/[id]/client-procedures` | | | |
| `/admin/transit-offices/[id]/configuracion` | | | |
| `/admin/transit-offices/[id]/documents` | | | |
| `/admin/transit-offices/[id]/imprint-validation` | | | |
| `/admin/transit-offices/[id]/mandatos` | | | |
| `/admin/transit-offices/[id]/portal-persona-natural` | | | |
| `/admin/transit-offices/[id]/reportes` | | | |
| `/admin/transit-offices/[id]/requirements` | | | |
| `/admin/transit-offices/[id]/revocation-requests` | | | |
| `/admin/transit-offices/[id]/rules` | | | |
| `/admin/transit-offices/[id]/usuarios` | | | |
| `/admin/transit-offices/[id]/webhooks` | | | |
| `/auth/forgot-password` | | | |
| `/auth/listo` | | | |
| `/auth/reset-password` | | | |
| `/biometric/[token]` | | | |
| `/invite/activate` | | | |
| `/log-qx/[submissionId]` | | | |
| `/login` | | | |
| `/manual` | | | |
| `/manual/[...slug]` | | | |
| `/portal/[token]` | | | |
| `/profile/change-password` | | | |
| `/tramites` | | | |
| `/tramites/[instanceId]` | | | |
| `/tramites/nuevo` | | | |
| `/tramites/nuevo/[procedureTypeCode]` | | | |
| `/tramites/revocatorias` | | | |
| `/verificacion/[token]` | | | |

## frontend-comparendos: rutas

| Ruta | C | O | M |
|---|---|---|---|
| `/(app)` | | | |
| `/(app)/admin/alertas` | | | |
| `/(app)/admin/calendario` | | | |
| `/(app)/admin/empresas` | | | |
| `/(app)/admin/ingesta` | | | |
| `/(app)/admin/log-migracion` | | | |
| `/(app)/admin/municipios` | | | |
| `/(app)/admin/ocr/prompts` | | | |
| `/(app)/admin/organismos` | | | |
| `/(app)/admin/plantillas` | | | |
| `/(app)/admin/scheduler` | | | |
| `/(app)/admin/tipologias` | | | |
| `/(app)/configuracion/empresa` | | | |
| `/(app)/configuracion/monitoreo` | | | |
| `/(app)/configuracion/organismos` | | | |
| `/(app)/configuracion/plantillas` | | | |
| `/(app)/configuracion/representantes` | | | |
| `/(app)/dp/lotes` | | | |
| `/(app)/mensajes` | | | |
| `/(app)/ocr` | | | |
| `/(app)/ocr/documentos/[id]` | | | |
| `/(app)/ocr/paquetes/[id]` | | | |
| `/(app)/pagos` | | | |
| `/(app)/reportes` | | | |
| `/(app)/reportes/productividad` | | | |
| `/403` | | | |
| `/portal/[token]` | | | |

## frontend-diagnostico: rutas

| Ruta | C | O | M |
|---|---|---|---|
| `/(app)` | | | |
| `/(app)/backoffice/diagnosticos` | | | |
| `/(app)/backoffice/diagnosticos/[id]` | | | |
| `/(app)/backoffice/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]` | | | |
| `/(app)/backoffice/fuentes-obligatorias` | | | |
| `/(app)/configuracion` | | | |
| `/(app)/diagnosticos/[id]` | | | |
| `/(app)/diagnosticos/[id]/carga` | | | |
| `/(app)/diagnosticos/[id]/vehiculos/[vehiculoId]/consultas/[fuente]` | | | |
| `/(app)/diagnosticos/nuevo` | | | |
| `/403` | | | |

## frontend-hub: rutas

| Ruta | C | O | M |
|---|---|---|---|
| `/` | | | |
| `/(auth)/auth/forgot-password` | | | |
| `/(auth)/auth/reset-password` | | | |
| `/(auth)/invite/activate` | | | |
| `/(auth)/login` | | | |
| `/403` | | | |
| `/proximamente/[producto]` | | | |

## Modales y diálogos (archivos que importan Modal)

| Archivo | C | O | M |
|---|---|---|---|
| `frontend-comparendos/app/(app)/admin/calendario/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/empresas/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/ingesta/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/municipios/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/ocr/prompts/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/organismos/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/plantillas/page.tsx` | | | |
| `frontend-comparendos/app/(app)/admin/tipologias/page.tsx` | | | |
| `frontend-comparendos/app/(app)/configuracion/monitoreo/page.tsx` | | | |
| `frontend-comparendos/app/(app)/configuracion/plantillas/page.tsx` | | | |
| `frontend-comparendos/app/(app)/configuracion/representantes/page.tsx` | | | |
| `frontend-comparendos/app/(app)/dp/lotes/page.tsx` | | | |
| `frontend-comparendos/app/(app)/mensajes/page.tsx` | | | |
| `frontend-comparendos/app/(app)/ocr/documentos/[id]/page.tsx` | | | |
| `frontend-comparendos/app/(app)/ocr/page.tsx` | | | |
| `frontend-comparendos/app/(app)/ocr/paquetes/[id]/page.tsx` | | | |
| `frontend-comparendos/app/(app)/page.tsx` | | | |
| `frontend-comparendos/app/(app)/reportes/productividad/page.tsx` | | | |
| `frontend-comparendos/components/HistorialAuditoria.tsx` | | | |
| `frontend-comparendos/components/bandeja/EnvioMasivoAviso.tsx` | | | |
| `frontend-comparendos/components/bandeja/EnvioMasivoDp.tsx` | | | |
| `frontend-comparendos/components/consultas/ConsultasAdmin.tsx` | | | |
| `frontend-comparendos/components/detalle/AccionesAviso.tsx` | | | |
| `frontend-comparendos/components/detalle/AccionesCambio.tsx` | | | |
| `frontend-comparendos/components/detalle/AccionesConductor.tsx` | | | |
| `frontend-comparendos/components/detalle/AccionesDp.tsx` | | | |
| `frontend-comparendos/components/detalle/AccionesPago.tsx` | | | |
| `frontend-comparendos/components/detalle/DetalleComparendo.tsx` | | | |
| `frontend-diagnostico/components/backoffice/FormularioLlenadoManual.tsx` | | | |
| `frontend-diagnostico/components/tablero/PanelDictamen.tsx` | | | |
| `frontend/app/tramites/page.tsx` | | | |
| `frontend/components/admin/banners/BannerDeleteDialog.tsx` | | | |
| `frontend/components/admin/banners/BannerFormPanel.tsx` | | | |
| `frontend/components/admin/banners/BannerListTable.tsx` | | | |
| `frontend/components/admin/branding/BrandingPreview.tsx` | | | |
| `frontend/components/admin/branding/BrandingPublishDialog.tsx` | | | |
| `frontend/components/admin/companies/AuditLogTable.tsx` | | | |
| `frontend/components/admin/companies/ChildCompanyStatusDialog.tsx` | | | |
| `frontend/components/admin/companies/CompanyStatusDialog.tsx` | | | |
| `frontend/components/admin/companies/CreateChildCompanyDialog.tsx` | | | |
| `frontend/components/admin/companies/CreateCompanyDialog.tsx` | | | |
| `frontend/components/admin/companies/EditCompanyDialog.tsx` | | | |
| `frontend/components/admin/companies/LinkCompanyDialog.tsx` | | | |
| `frontend/components/admin/companies/OTConfigModal.tsx` | | | |
| `frontend/components/admin/companies/OtScopeConfirmDialog.tsx` | | | |
| `frontend/components/admin/companies/PersonalizedDocumentsPanel.tsx` | | | |
| `frontend/components/admin/companies/SaveConfigDialog.tsx` | | | |
| `frontend/components/admin/companies/UnlinkCompanyDialog.tsx` | | | |
| `frontend/components/admin/companies/deeds/DeedsFormPanel.tsx` | | | |
| `frontend/components/admin/companies/external-integrations/ExternalIntegrationActionDialog.tsx` | | | |
| `frontend/components/admin/companies/external-integrations/ExternalIntegrationFormModal.tsx` | | | |
| `frontend/components/admin/companies/external-integrations/ExternalIntegrationSecretModal.tsx` | | | |
| `frontend/components/admin/companies/legal-representatives/LegalRepresentativesFormPanel.tsx` | | | |
| `frontend/components/admin/companies/legal-representatives/LegalRepresentativesTab.tsx` | | | |
| `frontend/components/admin/companies/mandate-signers/CompanyMandatarioForm.tsx` | | | |
| `frontend/components/admin/companies/mandate-signers/FormatosContratoModal.tsx` | | | |
| `frontend/components/admin/companies/mandate-signers/MandatarioBajaDialog.tsx` | | | |
| `frontend/components/admin/companies/mandate-signers/MandatarioOrganismos.tsx` | | | |
| `frontend/components/admin/companies/signature-vault/SignatureVaultDetailModal.tsx` | | | |
| `frontend/components/admin/companies/signature-vault/SignatureVaultEditPanel.tsx` | | | |
| `frontend/components/admin/companies/signature-vault/SignatureVaultFormPanel.tsx` | | | |
| `frontend/components/admin/companies/signature-vault/SignatureVaultTab.tsx` | | | |
| `frontend/components/admin/documents/ConfirmDeleteDocumentTypeDialog.tsx` | | | |
| `frontend/components/admin/documents/CreateDocumentTypeDialog.tsx` | | | |
| `frontend/components/admin/documents/DocumentInUseDialog.tsx` | | | |
| `frontend/components/admin/domain/NetworkDomainsSection.tsx` | | | |
| `frontend/components/admin/jobs/JobsComoFunciona.tsx` | | | |
| `frontend/components/admin/plataforma/CompanyTipoMandatoModal.tsx` | | | |
| `frontend/components/admin/plataforma/MandatoFormatoEditor.tsx` | | | |
| `frontend/components/admin/plataforma/MandatoOtConfigForm.tsx` | | | |
| `frontend/components/admin/plataforma/MandatosCatalogPanel.tsx` | | | |
| `frontend/components/admin/plataforma/NotificacionEnviarPruebaModal.tsx` | | | |
| `frontend/components/admin/plataforma/NotificacionVistaPreviaModal.tsx` | | | |
| `frontend/components/admin/plataforma/ai/AiToolCard.tsx` | | | |
| `frontend/components/admin/plataforma/mensajes-muertos/MensajesMuertosPanel.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/EnlaceQrDialog.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/TarifaFormModal.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/TarifaHistorialModal.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/contenido/BannerFormModal.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/contenido/EnlacesSection.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/contenido/PreguntaFormModal.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/contenido/VersionesModal.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/contenido/VistaPreviaModal.tsx` | | | |
| `frontend/components/admin/plataforma/persona-natural/pn-componentes.tsx` | | | |
| `frontend/components/admin/transit-offices/CreateTransitOfficeTenantDialog.tsx` | | | |
| `frontend/components/admin/transit-offices/MandatarioFirmaPreviewDialog.tsx` | | | |
| `frontend/components/admin/transit-offices/OtImprintValidationSection.tsx` | | | |
| `frontend/components/admin/transit-offices/OtSidePanel.tsx` | | | |
| `frontend/components/admin/transit-offices/TransitOfficeQuipuxDialog.tsx` | | | |
| `frontend/components/admin/transit-offices/TransitOfficeStatusDialog.tsx` | | | |
| `frontend/components/admin/transit-offices/portal-pn/PortalPnDesignSection.tsx` | | | |
| `frontend/components/atom/modules/ManualImageGallery.tsx` | | | |
| `frontend/components/atom/modules/ManualReviewActions.tsx` | | | |
| `frontend/components/atom/modules/ManualReviewDetailModal.tsx` | | | |
| `frontend/components/atom/modules/PrevalidacionDetailDrawer.tsx` | | | |
| `frontend/components/atom/modules/RbacAdmin.tsx` | | | |
| `frontend/components/atom/modules/_reportes/ProcedureDetailPanel.tsx` | | | |
| `frontend/components/atom/modules/users/CancelInvitationDialog.tsx` | | | |
| `frontend/components/atom/modules/users/CompanyRoleFormModal.tsx` | | | |
| `frontend/components/atom/modules/users/CompanyRolesPanel.tsx` | | | |
| `frontend/components/atom/modules/users/DeleteUserDialog.tsx` | | | |
| `frontend/components/atom/modules/users/EditUserModal.tsx` | | | |
| `frontend/components/atom/modules/users/ExternalClientActionDialog.tsx` | | | |
| `frontend/components/atom/modules/users/ExternalClientFormModal.tsx` | | | |
| `frontend/components/atom/modules/users/ResetPasswordDialog.tsx` | | | |
| `frontend/components/atom/modules/users/RestoreUserDialog.tsx` | | | |
| `frontend/components/atom/modules/users/SuspendOrDeactivateModal.tsx` | | | |
| `frontend/components/atom/modules/users/UserAuditHistoryDrawer.tsx` | | | |
| `frontend/components/atom/modules/users/UserPermissionsModal.tsx` | | | |
| `frontend/components/consultas/QueryConsole.tsx` | | | |
| `frontend/components/consultas/SavedQueryList.tsx` | | | |
| `frontend/components/operacion/ActorsForm.tsx` | | | |
| `frontend/components/operacion/AdminTramiteAcciones.tsx` | | | |
| `frontend/components/operacion/CargaMasivaModal.tsx` | | | |
| `frontend/components/operacion/DescargaMasivaConfirmModal.tsx` | | | |
| `frontend/components/operacion/IdentidadParteTrackingModal.tsx` | | | |
| `frontend/components/operacion/ReenviarValidacionIdentidadModal.tsx` | | | |
| `frontend/components/operacion/TramiteDocumentosModal.tsx` | | | |
| `frontend/components/operacion/TramiteTrackingModal.tsx` | | | |
| `frontend/components/operacion/TramiteWizard.tsx` | | | |
| `frontend/components/operacion/TramitesTable.tsx` | | | |
| `frontend/components/operacion/VencerIdentidadModal.tsx` | | | |
| `frontend/components/shared/DocumentPreviewModal.tsx` | | | |
| `frontend/components/shared/permissions/ModulePermissionsSection.tsx` | | | |

## Transversales

| Ítem | C | O | M |
|---|---|---|---|
| Ingreso (login) y recuperación de contraseña | | | |
| Dock nuevo: cada grupo y submenú, por rol (SuperAdmin, gestor, ot_admin, radicador) | | | |
| Selector de productos y menú de cuenta | | | |
| Ctrl K: secciones, acciones, placas y teclado | | | |
| Acordeón y cifras de Inicio | | | |
| Estados vacío, de carga y de error de cada tipo de lista | | | |
| Páginas 403 y 404 | | | |
| Favicon y logos por app | | | |
| Marca blanca (host de socio) | | | |
| Manual y dr-flit | | | |
| Pasos del asistente de traspaso y de matrícula (solo aspecto) | | | |
| Correo de prueba con los logos nuevos (incluido Renting) | | | |
