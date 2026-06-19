namespace Flit.Infrastructure.Email;

/// <summary>Configuración SMTP (sección "Smtp" de appsettings).</summary>
public sealed class EmailSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    public bool DisableAuthentication { get; set; }

    public string DefaultSenderEmail { get; set; } = string.Empty;

    public string DefaultSenderPassword { get; set; } = string.Empty;

    public string DefaultSenderName { get; set; } = "FLIT Trámites";
}
