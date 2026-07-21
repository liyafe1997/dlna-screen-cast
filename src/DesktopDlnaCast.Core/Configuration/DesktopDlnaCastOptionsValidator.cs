using Microsoft.Extensions.Options;

namespace DesktopDlnaCast.Core.Configuration;

internal sealed class DesktopDlnaCastOptionsValidator : IValidateOptions<DesktopDlnaCastOptions>
{
    public ValidateOptionsResult Validate(string? name, DesktopDlnaCastOptions options)
    {
        if (options.NetworkTimeout <= TimeSpan.Zero || options.NetworkTimeout > TimeSpan.FromMinutes(1))
        {
            return ValidateOptionsResult.Fail("NetworkTimeout must be between zero and one minute.");
        }

        if (options.MaximumXmlResponseBytes is < 1024 or > 4 * 1024 * 1024)
        {
            return ValidateOptionsResult.Fail("MaximumXmlResponseBytes must be between 1 KiB and 4 MiB.");
        }

        if (options.MaximumDiagnosticTextLength is < 1024 or > 64 * 1024)
        {
            return ValidateOptionsResult.Fail("MaximumDiagnosticTextLength must be between 1 KiB and 64 KiB.");
        }

        return ValidateOptionsResult.Success;
    }
}

