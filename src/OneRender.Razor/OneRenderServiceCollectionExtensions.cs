using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;

namespace OneRender.Razor;

/// <summary>Registers the renderer with dependency injection.</summary>
public static class OneRenderServiceCollectionExtensions
{
    /// <summary>
    /// Adds a singleton <see cref="RazorRenderer"/> that serves the application's web root and static web assets
    /// (<c>IWebHostEnvironment.WebRootFileProvider</c>) to the engine.
    /// </summary>
    /// <param name="services">The collection.</param>
    /// <param name="configure">Changes the <see cref="RazorRenderOptions"/>; null for the defaults.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// Without a web host (a console app) the renderer is built from an <see cref="IFileProvider"/> registered in the collection, and
    /// resolving it throws <see cref="InvalidOperationException"/> if there is neither. Calling this twice keeps the first registration.
    /// </remarks>
    public static IServiceCollection AddOneRender(this IServiceCollection services, Action<RazorRenderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new RazorRenderOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(sp =>
        {
            IFileProvider? root = sp.GetService<IWebHostEnvironment>()?.WebRootFileProvider ?? sp.GetService<IFileProvider>();
            return root is null
                ? throw new InvalidOperationException("OneRender needs a web root: run inside a web host or register an IFileProvider.")
                : new RazorRenderer(sp, root, options);
        });
        return services;
    }
}
