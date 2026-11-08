using UnityEditor;
using UnityEngine;

namespace Biplanes.EditorTools {

    /// <summary>
    /// Forces every PNG under Assets/Biplanes/Resources/Sprites to import as a single
    /// Sprite (ARCHITECTURE §6: Sprite 2D, PPU 100, Bilinear, uncompressed).
    ///
    /// Why: textures imported in "Multiple" sprite mode store their sprites as sub-assets,
    /// so Resources.Load&lt;Sprite&gt;("Sprites/...") returns null and every entity falls back
    /// to a white placeholder. ProjectBuilder.ConfigureProject applies the same settings,
    /// but only when someone runs it; this postprocessor applies them on every import,
    /// including PNGs that tools/rasterize.py adds later.
    ///
    /// Bump GetVersion() whenever these settings change; Unity then reimports the
    /// affected textures automatically.
    /// </summary>
    public class SpriteImportSettings : AssetPostprocessor {

        private const string SpritesRoot = "Assets/Biplanes/Resources/Sprites/";

        public override uint GetVersion() => 1;

        private void OnPreprocessTexture() {
            if (!assetPath.StartsWith(SpritesRoot) || !assetPath.EndsWith(".png")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.filterMode          = FilterMode.Bilinear;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
        }
    }
}
