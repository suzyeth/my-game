using UnityEditor;
using UnityEngine;
using System.IO;

namespace FartSymphony.Editor
{
    /// <summary>
    /// Generates solid-colour placeholder sprites for Sprint 2 UI.
    /// Menu: FartSymphony → Generate Placeholder Assets
    /// After generation, rebuilds the Sprint2Dev scene automatically.
    /// </summary>
    public static class PlaceholderAssetGenerator
    {
        private const string OUT_DIR = "Assets/art/placeholders";

        [MenuItem("FartSymphony/Generate Placeholder Assets")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(OUT_DIR))
                AssetDatabase.CreateFolder("Assets/art", "placeholders");

            // ── Background: dark stage with spotlight gradient ────────────────
            SaveTex("bg.png",             MakeGradientV(1920, 1080,
                new Color(0.05f, 0.03f, 0.12f), new Color(0.15f, 0.08f, 0.25f)));

            // ── Note track: semi-transparent dark bar ────────────────────────
            SaveTex("note_track.png",     MakeSolid(1024, 96,
                new Color(0.1f, 0.1f, 0.15f, 0.85f)));

            // ── Judgment line: bright yellow vertical bar ────────────────────
            SaveTex("judgment_line.png",  MakeSolid(8, 96,
                new Color(1f, 0.9f, 0.1f, 1f)));

            // ── Note icon: glowing white circle ──────────────────────────────
            SaveTex("note_icon.png",      MakeCircle(64, Color.white,
                new Color(0.8f, 0.8f, 1f, 0f)));

            // ── Bloat gauge background: dark bordered rectangle ───────────────
            SaveTex("bloat_bg.png",       MakeBorderedRect(80, 400,
                new Color(0.05f, 0.05f, 0.1f, 1f),
                new Color(0.4f, 0.4f, 0.5f, 1f), 4));

            // ── Bloat gauge fill: white (tinted green→red by VisualCueSystem) ─
            SaveTex("bloat_fill.png",     MakeSolid(72, 392, Color.white));

            // ── Bloat gauge top cap: bright accent bar ────────────────────────
            SaveTex("bloat_top.png",      MakeSolid(80, 16,
                new Color(0.9f, 0.9f, 1f, 1f)));

            // ── Popup sprites: coloured text-box backgrounds ──────────────────
            SaveTex("popup_perfect.png",  MakeRoundedRect(256, 64,
                new Color(1f, 0.85f, 0f, 0.9f)));
            SaveTex("popup_good.png",     MakeRoundedRect(256, 64,
                new Color(0.3f, 0.8f, 1f, 0.9f)));
            SaveTex("popup_miss.png",     MakeRoundedRect(256, 64,
                new Color(1f, 0.2f, 0.1f, 0.9f)));

            AssetDatabase.Refresh();

            // Set all as Single sprites
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { OUT_DIR }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp  = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                imp.textureType      = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.filterMode       = FilterMode.Bilinear;
                imp.SaveAndReimport();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[PlaceholderAssetGenerator] Done — assets in {OUT_DIR}");

            // Rebuild the scene with placeholder paths
            Sprint2SceneBuilder.BuildWithPlaceholders();
        }

        // ── Texture helpers ────────────────────────────────────────────────────

        private static void SaveTex(string name, Texture2D tex)
        {
            byte[] png  = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            string path = Path.Combine(Application.dataPath, "art", "placeholders", name);
            File.WriteAllBytes(path, png);
        }

        private static Texture2D MakeSolid(int w, int h, Color c)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = c;
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private static Texture2D MakeGradientV(int w, int h, Color top, Color bot)
        {
            var t   = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pix = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float f = (float)y / (h - 1);
                Color c = Color.Lerp(bot, top, f);
                for (int x = 0; x < w; x++)
                    pix[y * w + x] = c;
            }
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private static Texture2D MakeCircle(int size, Color inner, Color outer)
        {
            var t   = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pix = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / r;
                pix[y * size + x] = d <= 1f
                    ? Color.Lerp(inner, outer, d)
                    : new Color(0, 0, 0, 0);
            }
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private static Texture2D MakeBorderedRect(int w, int h, Color fill, Color border, int bw)
        {
            var t   = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pix = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x < bw || x >= w - bw || y < bw || y >= h - bw;
                pix[y * w + x] = edge ? border : fill;
            }
            t.SetPixels(pix);
            t.Apply();
            return t;
        }

        private static Texture2D MakeRoundedRect(int w, int h, Color c)
        {
            var t   = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pix = new Color[w * h];
            float r = h * 0.4f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Clamp to rounded corners
                float cx = Mathf.Clamp(x, r, w - r);
                float cy = Mathf.Clamp(y, r, h - r);
                float d  = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                pix[y * w + x] = d <= r ? c : new Color(0, 0, 0, 0);
            }
            t.SetPixels(pix);
            t.Apply();
            return t;
        }
    }
}
