namespace Flit.Admin.Application.Auditing;

/// <summary>
/// Vocabulario estable de la auditoría de configuración y administrativa/seguridad (RNF01,
/// ADR-0024; HU #10678). Centraliza los valores de <c>result</c>, <c>operation</c> y
/// <c>module</c> que persisten en <c>admin.tenant_config_audit_logs</c> para que writers de
/// éxito, writer de fallo, <c>IAdminAuditWriter</c> y el filtro de la API usen exactamente
/// las mismas cadenas.
/// </summary>
public static class AuditVocabulary
{
    /// <summary>Desenlace de la operación auditada.</summary>
    public static class Results
    {
        public const string Success = "success";
        public const string Failure = "failure";
    }

    /// <summary>Verbo explícito de la operación auditada.</summary>
    public static class Operations
    {
        public const string Create = "create";
        public const string Update = "update";
        public const string Delete = "delete";

        // HU #10678 — operaciones administrativas y de seguridad transversales.
        public const string AssignRole = "assign_role";
        public const string RevokeRole = "revoke_role";
        public const string Suspend = "suspend";
        public const string Unsuspend = "unsuspend";
        public const string Invite = "invite";
        public const string ResendInvite = "resend_invite";
        public const string ReactivateInvite = "reactivate_invite";
        public const string DeleteUser = "delete_user";
        public const string Login = "login";
        public const string LoginFailed = "login_failed";
        public const string Logout = "logout";
        public const string ForgotPassword = "forgot_password";
        public const string ResetPassword = "reset_password";
        public const string ChangePassword = "change_password";
        public const string AdminResetPassword = "admin_reset_password";
        public const string ActivateAccount = "activate_account";

        // HU #11320 — documentos personalizados por compañía (Feature #11309, ADR-0042): las
        // cuatro operaciones auditadas también a nivel de aplicación (el trigger de BD sobre
        // admin.company_personalized_documents cubre la fila, esto cubre actor + resultado).
        public const string Confirm = "confirm";
        public const string Activate = "activate";
        public const string Deactivate = "deactivate";
    }

    /// <summary>Categoría transversal de la operación auditada (HU #10678).</summary>
    public static class Modules
    {
        public const string Users = "users";
        public const string Roles = "roles";
        public const string Permissions = "permissions";
        public const string Authentication = "authentication";
        public const string Security = "security";
        public const string Config = "config";

        // HU #11320 — documentos personalizados por compañía (Feature #11309).
        public const string Companies = "companies";

        // HU #11366 — buzón de pruebas de notificaciones (Feature #11349).
        public const string Notifications = "notifications";
    }
}
