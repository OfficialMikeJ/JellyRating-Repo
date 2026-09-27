using Jellyfin.Plugin.ParentalRatingManager.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ParentalRatingManager;

/// <summary>
/// Registers plugin services in Jellyfin's dependency injection container.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ICustomRatingsSource, PluginCustomRatingsSource>();
        serviceCollection.AddSingleton<RatingCatalog>();
        serviceCollection.AddSingleton<RatingNormalizer>();
        serviceCollection.AddSingleton<PluginDataStore>();
        serviceCollection.AddSingleton<EffectiveRatingService>();
        serviceCollection.AddSingleton<RatingAssignmentService>();
        serviceCollection.AddSingleton<UserRestrictionService>();
        serviceCollection.AddSingleton<LibraryContentService>();
    }
}
