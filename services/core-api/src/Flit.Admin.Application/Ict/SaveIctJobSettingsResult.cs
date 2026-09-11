using Flit.Admin.Domain.Ict;

namespace Flit.Admin.Application.Ict;

public sealed record SaveIctJobSettingsResult(
    bool IsValid,
    IctJobSettingsView? Settings,
    IReadOnlyList<IctJobSettingsFieldError> Errors)
{
    public static SaveIctJobSettingsResult Ok(IctJobSettingsView settings) =>
        new(true, settings, []);

    public static SaveIctJobSettingsResult Invalid(IReadOnlyList<IctJobSettingsFieldError> errors) =>
        new(false, null, errors);
}
