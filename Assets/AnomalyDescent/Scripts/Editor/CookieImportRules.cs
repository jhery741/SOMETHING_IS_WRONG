using UnityEditor;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>Importa las texturas de haz de luz (nombre "*_Cookie") como Cookie: escala de grises → alfa, Clamp, con mipmaps.</summary>
    public class CookieImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!System.IO.Path.GetFileNameWithoutExtension(assetPath).EndsWith("_Cookie")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Cookie;
            ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.filterMode = FilterMode.Trilinear;
        }
    }
}
