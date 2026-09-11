using UnityEditor;
using UnityEngine;

namespace SipSorcery.Tests.EditorScripts
{
    /// <summary>
    /// SIPSorcery's netstandard2.1 publish output drops a few managed
    /// assemblies into Assets/Plugins that carry platform-conditional
    /// references (notably Makaretu.Dns.Multicast -> Tmds.LibC, which is
    /// Linux-only). Unity's plugin importer rejects the whole assembly when
    /// a reference cannot be resolved, which in turn prevents SIPSorcery
    /// itself from loading and leaves the smoke test with 0 discovered tests.
    ///
    /// Turning off reference validation on every plugin under Assets/Plugins
    /// matches what the issue reporter (and Unity users in general) end up
    /// doing manually via the Plugin Inspector. The unresolved references
    /// only matter for code paths that never execute on Windows/macOS, so
    /// runtime behaviour is unaffected.
    /// </summary>
    [InitializeOnLoad]
    public static class PluginImportSettings
    {
        static PluginImportSettings()
        {
            EditorApplication.delayCall += ApplyOnce;
        }

        // Callable from the command line via:
        //   Unity -batchmode -executeMethod SipSorcery.Tests.EditorScripts.PluginImportSettings.Apply
        // Used by the unity-smoke-test workflow's settle pass so plugin
        // import settings are deterministic before the test pass runs.
        public static void Apply()
        {
            ApplyOnce();
            EditorApplication.Exit(0);
        }

        // AssetPostprocessor variant: runs during plugin import (before Unity
        // decides whether the assembly is loadable), which is the only time
        // we can prevent the "Reference has errors" cascade in a single Unity
        // session.
        internal sealed class PluginPreprocessor : AssetPostprocessor
        {
            private void OnPreprocessAsset()
            {
                if (!assetPath.StartsWith("Assets/Plugins/")) { return; }
                if (!assetPath.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase)) { return; }
                var importer = assetImporter as PluginImporter;
                if (importer == null) { return; }
                FixImporter(importer, logChanges: false);
            }
        }

        private static void ApplyOnce()
        {
            EditorApplication.delayCall -= ApplyOnce;
            bool changed = false;
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/Plugins/")) { continue; }
                if (!path.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase)) { continue; }
                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                if (importer == null) { continue; }
                if (FixImporter(importer, logChanges: true))
                {
                    importer.SaveAndReimport();
                    changed = true;
                }
            }
            if (changed) { AssetDatabase.Refresh(); }
        }

        private static bool FixImporter(PluginImporter importer, bool logChanges)
        {
            bool fixedAnything = false;

            // PluginImporter.ValidateReferences is internal in Unity 6, so
            // poke the underlying serialized field instead.
            var so = new SerializedObject(importer);
            var prop = so.FindProperty("m_ValidateReferences");
            if (prop != null && prop.boolValue)
            {
                prop.boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                fixedAnything = true;
            }

            // When validation initially failed, Unity disables the Editor
            // platform on the plugin. Re-enable broad compatibility so the
            // assembly actually loads.
            if (!importer.GetCompatibleWithAnyPlatform() ||
                !importer.GetCompatibleWithEditor())
            {
                importer.SetCompatibleWithAnyPlatform(true);
                importer.SetCompatibleWithEditor(true);
                fixedAnything = true;
            }

            if (fixedAnything && logChanges)
            {
                Debug.Log($"[SipSorcery smoke test] Fixed plugin import settings for {importer.assetPath}");
            }
            return fixedAnything;
        }
    }
}
