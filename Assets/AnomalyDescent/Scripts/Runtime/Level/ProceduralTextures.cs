using UnityEngine;
namespace SomethingIsWrong
{
    // Grano del HUD. El arte de la casa se guarda en assets.
    public static class ProceduralTextures
    {
        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Ruido para el grano de película del HUD.</summary>
        public static Texture2D Grain()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            t.name = "FilmGrain";
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Point;
            var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                float g = Hash(i % S, i / S, 3);
                px[i] = new Color(g, g, g, Mathf.Abs(g - 0.5f) * 2f);
            }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

    }
}

