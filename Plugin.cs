using Stellar.Abstractions.Plugins;
using Stellar.Abstractions.Services;

namespace Stellar.PhotoStudio;

/// <summary>
/// Photo Studio — hide the HUD/nameplates/other players, capture a supersampled screenshot, and
/// grade the shot with a look built from the game's own render-pipeline volumes (depth of field,
/// colour, white balance, LUT, bloom, vignette, film grain). UI, presets and orchestration only;
/// the actual capture/look/visibility mechanics live in the framework.
/// </summary>
public sealed partial class Plugin : IStellarPlugin
{
    private readonly IPluginServices _services;
    private readonly ILocalization _loc;

    public Plugin(IPluginServices services)
    {
        _services = services;
        _loc = services.Localization;
    }

    public string Name => "Photo Studio";

    public void Dispose()
    {
    }
}
