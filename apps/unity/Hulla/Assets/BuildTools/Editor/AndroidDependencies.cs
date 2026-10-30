using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Resolves the Android libraries declared in *Dependencies.xml (Meta's SDK, Credential Manager) into
    /// Plugins/Android/mainTemplate.gradle, in batch mode too. The External Dependency Manager loads its
    /// resolver late, so it is found by name once loaded. Batch: -executeMethod Hulla.BuildTools.AndroidDependencies.Resolve
    /// </summary>
    public static class AndroidDependencies
    {
        [MenuItem("Hulla/Resolve Android dependencies")]
        public static void Resolve()
        {
            var resolver = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("GooglePlayServices.PlayServicesResolver", false))
                .FirstOrDefault(t => t != null);
            if (resolver == null)
            {
                throw new InvalidOperationException("the External Dependency Manager's Android resolver is not loaded");
            }

            var sync = resolver.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null)
                ?? throw new InvalidOperationException("PlayServicesResolver.ResolveSync(bool) not found");
            var ok = (bool)sync.Invoke(null, new object[] { true });
            Debug.Log($"Android dependencies resolved: {ok}");
            if (!ok)
            {
                throw new InvalidOperationException("Android dependency resolution failed");
            }
        }
    }
}
