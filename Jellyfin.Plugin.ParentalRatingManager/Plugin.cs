using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ParentalRatingManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ParentalRatingManager;

/// <summary>
/// Plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The stable plugin GUID used by the repository manifest.</summary>
    public const string PluginGuid = "77304605-bb68-4844-8aed-251df4e20e83";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>The current plugin instance (set during construction).</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Parental Rating Manager";

    /// <inheritdoc />
    public override string Description =>
        "Manage Canadian and US parental ratings for media and enforce per-user maximum rating restrictions.";

    /// <inheritdoc />
    public override Guid Id { get; } = new Guid(PluginGuid);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "ParentalRatingManager",
            DisplayName = "Parental Rating Manager",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
