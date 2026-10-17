using System.Runtime.CompilerServices;
using System.Text;

namespace Bank.Services;

/// <summary>
/// Registers the legacy code-page encoding provider (e.g. code page 1252) required by
/// ExcelDataReader's default configuration. Running it from a module initializer means it
/// executes as soon as the Bank.Services assembly is loaded, so every host/entry point is
/// covered without having to remember to register it in Program.cs.
/// </summary>
internal static class EncodingSetup
{
    // Intentional: registering here guarantees the provider is available no matter which
    // host loads Bank.Services, so we opt out of the "application code only" advisory.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
#pragma warning restore CA2255
}
