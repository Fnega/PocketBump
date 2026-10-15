using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PocketBump.Editor
{
    // Explicit incremental glyph repair. No scene rewrite, font substitution, palette,
    // model, or layout changes; keep a recoverable byte-for-byte source-asset backup.
    public static class DashNetworkWaitFontBaker
    {
        [MenuItem("Tools/Pocket Bump/Dash/Add Input Wait Font Glyphs (Incremental)")]
        public static void AddInputWaitGlyphs()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before changing font assets.");
            string path = DashProductionSceneBaker.FontAssetPath;
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null) throw new FileNotFoundException("The approved existing font asset is required.");
            if (font.HasCharacters(DashMatchHud.RequiredChineseGlyphs, out uint[] missing, false, false))
            { Debug.Log("Input-wait glyphs already exist; no font or scene change."); return; }
            string backup = Path.Combine(".art-source", "font-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(backup);
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
            if (File.Exists(path + ".meta")) File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"), false);
            if (!font.TryAddCharacters(missing, out uint[] unavailable))
                throw new InvalidOperationException("Approved source font lacks required input-wait glyphs; backup: " + backup);
            foreach (Texture2D atlas in font.atlasTextures)
            {
                if (atlas == null) continue;
                if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
                EditorUtility.SetDirty(atlas);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            if (!font.HasCharacters(DashMatchHud.RequiredChineseGlyphs, out unavailable, false, false))
                throw new InvalidOperationException("Incremental font glyph verification failed.");
            Debug.Log("Added only missing input-wait glyphs to the existing approved font. Backup: " + Path.GetFullPath(backup));
        }
    }
}
