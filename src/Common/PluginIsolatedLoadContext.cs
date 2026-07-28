#if !NETFRAMEWORK
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Common
{
    // Equivalent to the PluginIsolatedLoadContext used by the Aquarius server host (Server.BusinessObjects.FieldDataPlugin),

    internal sealed class PluginIsolatedLoadContext : AssemblyLoadContext
    {
        private readonly string _pluginDirectory;
        private readonly AssemblyDependencyResolver _resolver;

        public PluginIsolatedLoadContext(string pluginDirectory, string mainAssemblyPath, string friendlyName)
            : base(name: friendlyName, isCollectible: true)
        {
            if (string.IsNullOrEmpty(pluginDirectory))
                throw new ArgumentNullException(nameof(pluginDirectory));
            if (string.IsNullOrEmpty(mainAssemblyPath))
                throw new ArgumentNullException(nameof(mainAssemblyPath));

            _pluginDirectory = pluginDirectory;

            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // First, probe the plugin directory. This ensures each plugin can ship its own
            // version of a dependency (e.g., Newtonsoft.Json) and get true side-by-side isolation.
            var probedPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(probedPath))
                return LoadFromAssemblyPath(probedPath);

            // Then fallback to the dependency resolver seeded from the plugin entry assembly.
            if (_resolver != null)
            {
                var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
                if (resolvedPath != null)
                    return LoadFromAssemblyPath(resolvedPath);
            }

            return null;
        }
    }
}
#endif