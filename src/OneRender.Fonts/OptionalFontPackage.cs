using System.IO;
using System.Reflection;

namespace OneRender.Fonts;

/// <summary>
/// The fonts of one opt-in fonts package (a Noto script, the CJK fonts or the emoji font): the faces, in the order they are registered,
/// and the package's licence and provenance files. Each package exposes one of these as <c>Package</c> on its own static class and
/// registers it with <see cref="OptionalFonts.Register(OptionalFontPackage)"/>.
/// </summary>
public sealed class OptionalFontPackage
{
    private readonly Assembly _assembly;

    /// <summary>Describes a package. Called by the fonts packages themselves.</summary>
    /// <param name="assembly">The package's assembly, which embeds the font files, <c>fonts/OFL.txt</c> and <c>fonts/MANIFEST.tsv</c>.</param>
    /// <param name="packageId">The NuGet package id, for example <c>RecursiveAI.OneRender.Fonts.Noto.Arabic</c>.</param>
    /// <param name="priority">Fallback tier: 0 for text scripts, 1 for color emoji. Within a tier packages are ordered by id, ordinally.</param>
    /// <param name="faces">The faces, in registration order.</param>
    public OptionalFontPackage(Assembly assembly, string packageId, int priority, IReadOnlyList<FontFaceInfo> faces)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(faces);
        _assembly = assembly;
        PackageId = packageId;
        Priority = priority;
        Faces = faces;
    }

    /// <summary>The NuGet package id.</summary>
    public string PackageId { get; }

    /// <summary>The fallback tier: lower tiers are tried first when no listed family covers a character.</summary>
    public int Priority { get; }

    /// <summary>The faces of the package, in registration order. The list is read-only.</summary>
    public IReadOnlyList<FontFaceInfo> Faces { get; }

    /// <summary>Opens the SIL Open Font License text (with the copyright lines) that covers the package's fonts.</summary>
    /// <returns>A new read-only stream over the licence file.</returns>
    public Stream OpenLicense() => FontFaceInfo.OpenFrom(_assembly, "fonts/OFL.txt");

    /// <summary>Opens the provenance manifest: tab-separated lines giving every shipped file's upstream URL and SHA-256; lines starting with <c>#</c> are comments.</summary>
    /// <returns>A new read-only stream over the UTF-8 manifest.</returns>
    public Stream OpenManifest() => FontFaceInfo.OpenFrom(_assembly, "fonts/MANIFEST.tsv");

    /// <inheritdoc />
    public override string ToString() => PackageId;
}

/// <summary>
/// The opt-in fonts packages registered in this process. A text service that builds its font collection from the packaged fonts
/// (<c>FontProviders</c> in <c>RecursiveAI.OneRender.Image</c>) adds the faces of every registered package after the default fonts, so
/// a character the default fonts lack falls back to the package that covers it. A package registers itself when the application
/// references it (its build adds a module initializer to the application) or when the host calls its <c>Register</c> method.
/// </summary>
public static class OptionalFonts
{
    private static readonly Lock s_lock = new();
    private static OptionalFontPackage[] s_packages = [];

    /// <summary>Increases every time a new package is registered; a cache of fonts built from <see cref="Packages"/> is stale when it changes.</summary>
    public static int Version { get; private set; }

    /// <summary>The registered packages in fallback order: by <see cref="OptionalFontPackage.Priority"/>, then by package id (ordinal), whatever the registration order.</summary>
    public static IReadOnlyList<OptionalFontPackage> Packages
    {
        get
        {
            lock (s_lock)
            {
                return s_packages;
            }
        }
    }

    /// <summary>Registers a package. Registering the same package id again does nothing.</summary>
    /// <param name="package">The package, usually its <c>Package</c> property.</param>
    public static void Register(OptionalFontPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        lock (s_lock)
        {
            foreach (OptionalFontPackage p in s_packages)
            {
                if (string.Equals(p.PackageId, package.PackageId, StringComparison.Ordinal))
                {
                    return;
                }
            }

            var next = new List<OptionalFontPackage>(s_packages) { package };
            next.Sort(static (a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : string.CompareOrdinal(a.PackageId, b.PackageId));
            s_packages = [.. next];
            Version++;
        }
    }
}

