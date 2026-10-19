using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Texturas generadas por código: papel tapiz, madera, baldosas, retratos, reloj...</summary>
    public static class ProceduralTextures
    {
        // ------------------------------------------------------------ ruido
        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

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

        static float VNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int X0 = Mod(x0, period), X1 = Mod(x0 + 1, period), Y0 = Mod(y0, period), Y1 = Mod(y0 + 1, period);
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Ruido fractal que se repite sin costuras. u,v en [0,1).</summary>
        static float Fbm(float u, float v, int basePeriod, int seed)
        {
            float s = 0f, amp = 0.5f;
            int p = basePeriod;
            for (int o = 0; o < 4; o++)
            {
                s += amp * VNoise(u * p, v * p, p, seed + o * 17);
                p *= 2;
                amp *= 0.5f;
            }
            return s / 0.9375f;
        }

        static Texture2D Make(int w, int h, string name, Color[] px, bool repeat = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.name = name;
            t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            t.SetPixels(px);
            t.Apply(true, false);
            return t;
        }

        static Color Opaque(Color c) { c.a = 1f; return c; }

        // ------------------------------------------------------------ superficies
        public static Texture2D Wallpaper(Color a, Color b, int seed)
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    int stripe = (x / 32) % 2;
                    Color c = stripe == 0 ? a : b;
                    float cx = (x % 32) - 15.5f;
                    float cy = ((y + stripe * 24) % 48) - 23.5f;
                    float dm = Mathf.Abs(cx) + Mathf.Abs(cy) * 0.7f;
                    if (dm < 7f) c = Color.Lerp(c, stripe == 0 ? b : a, 0.55f);
                    if (x % 32 == 0 || x % 32 == 31) c *= 0.8f;
                    float n = Fbm(u, v, 4, seed);
                    c *= 0.8f + 0.32f * n;
                    c *= 0.94f + 0.06f * Hash(x, y, seed + 3);
                    px[y * W + x] = Opaque(c);
                }
            return Make(W, H, "Wallpaper_" + seed, px);
        }

        public static Texture2D Wood(Color baseC, int seed)
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    int plank = y / 64;
                    float tint = Hash(plank, 7, seed);
                    float grain = Mathf.Sin(y * 0.35f + Fbm(u, v, 8, seed) * 12f) * 0.5f + 0.5f;
                    Color c = baseC * (0.75f + 0.35f * grain) * (0.85f + 0.3f * tint);
                    if (y % 64 < 2) c *= 0.45f;
                    int off = (int)(Hash(plank, 0, seed) * W);
                    if (((x + off) % W) < 2) c *= 0.5f;
                    px[y * W + x] = Opaque(c);
                }
            return Make(W, H, "Wood_" + seed, px);
        }

        public static Texture2D Tiles(Color a, Color b, int seed)
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            Color grout = new Color(0.32f, 0.31f, 0.29f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    int tx = x / 64, ty = y / 64;
                    Color c = ((tx + ty) % 2 == 0) ? a : b;
                    if (x % 64 < 3 || y % 64 < 3) c = grout;
                    c *= 0.85f + 0.25f * Fbm(u, v, 4, seed);
                    px[y * W + x] = Opaque(c);
                }
            return Make(W, H, "Tiles_" + seed, px);
        }

        public static Texture2D Plaster(Color baseC, int seed)
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    float n = Fbm(u, v, 4, seed);
                    float stain = Mathf.SmoothStep(0.62f, 0.75f, Fbm(u, v, 2, seed + 9));
                    Color c = baseC * (0.88f + 0.2f * n);
                    c = Color.Lerp(c, new Color(0.45f, 0.38f, 0.25f), stain * 0.45f);
                    px[y * W + x] = Opaque(c);
                }
            return Make(W, H, "Plaster_" + seed, px);
        }

        public static Texture2D Carpet(Color baseC, int seed)
        {
            const int W = 256, H = 256;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    float cx = (x % 64) - 31.5f, cy = (y % 64) - 31.5f;
                    float dm = Mathf.Abs(cx) + Mathf.Abs(cy);
                    Color c = baseC;
                    if (dm > 26f && dm < 30f) c *= 1.35f;
                    c *= 0.8f + 0.25f * Hash(x, y, seed) * 0.6f + 0.2f * Fbm(u, v, 8, seed);
                    px[y * W + x] = Opaque(c);
                }
            return Make(W, H, "Carpet_" + seed, px);
        }

        public static Texture2D Fabric(Color baseC, int seed)
        {
            const int W = 128, H = 128;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    float weave = ((x + y) % 4 < 2) ? 1f : 0.9f;
                    Color c = baseC * weave * (0.85f + 0.25f * Fbm(u, v, 4, seed));
                    px[y * W + x] = Opaque(c);
                }
            return Make(W, H, "Fabric_" + seed, px);
        }

        // ------------------------------------------------------------ dibujo
        static void FillEllipse(Color[] px, int W, int H, float cx, float cy, float rx, float ry, Color c)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + rx));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) px[y * W + x] = c;
                }
        }

        static void Dot(Color[] px, int W, int H, float x, float y, float r, Color c)
        {
            FillEllipse(px, W, H, x, y, r, r, c);
        }

        static void Line(Color[] px, int W, int H, float x0, float y0, float x1, float y1, float r, Color c)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Dot(px, W, H, Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), r, c);
            }
        }

        /// <summary>Retrato al óleo. La versión anómala cambia ojos, cejas y boca.</summary>
        public static Texture2D Portrait(bool anomalous)
        {
            const int W = 128, H = 160;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = Fbm((float)x / W, (float)y / H, 4, 77);
                    Color c = Color.Lerp(new Color(0.1f, 0.09f, 0.06f), new Color(0.27f, 0.23f, 0.15f), (float)y / H) * (0.8f + 0.3f * n);
                    px[y * W + x] = Opaque(c);
                }
            Color clothes = new Color(0.07f, 0.07f, 0.09f);
            Color skin = new Color(0.78f, 0.66f, 0.55f);
            Color skinDark = new Color(0.6f, 0.48f, 0.4f);
            Color hair = new Color(0.16f, 0.1f, 0.06f);
            FillEllipse(px, W, H, 64, 8, 54, 46, clothes);
            FillEllipse(px, W, H, 64, 52, 9, 14, skinDark);
            FillEllipse(px, W, H, 64, 106, 31, 28, hair);
            FillEllipse(px, W, H, 64, 88, 27, 35, skin);
            Line(px, W, H, 64, 92, 63, 82, 1.2f, skinDark);
            if (!anomalous)
            {
                FillEllipse(px, W, H, 53, 95, 5.5f, 3.2f, new Color(0.92f, 0.9f, 0.85f));
                FillEllipse(px, W, H, 75, 95, 5.5f, 3.2f, new Color(0.92f, 0.9f, 0.85f));
                Dot(px, W, H, 53, 95, 2.4f, new Color(0.15f, 0.1f, 0.06f));
                Dot(px, W, H, 75, 95, 2.4f, new Color(0.15f, 0.1f, 0.06f));
                Line(px, W, H, 47, 102, 59, 102, 1.1f, hair);
                Line(px, W, H, 69, 102, 81, 102, 1.1f, hair);
                for (int x = 56; x <= 72; x++)
                {
                    float y = 72f + 0.02f * (x - 64) * (x - 64);
                    Dot(px, W, H, x, y, 1.2f, new Color(0.5f, 0.25f, 0.22f));
                }
            }
            else
            {
                FillEllipse(px, W, H, 53, 95, 5.5f, 4.2f, new Color(0.02f, 0.02f, 0.02f));
                FillEllipse(px, W, H, 75, 95, 5.5f, 4.2f, new Color(0.02f, 0.02f, 0.02f));
                Dot(px, W, H, 54, 95, 0.9f, new Color(0.7f, 0.1f, 0.05f));
                Dot(px, W, H, 76, 95, 0.9f, new Color(0.7f, 0.1f, 0.05f));
                Line(px, W, H, 46, 105, 59, 100, 1.2f, hair);
                Line(px, W, H, 69, 100, 82, 105, 1.2f, hair);
                FillEllipse(px, W, H, 64, 70, 7f, 6.5f, new Color(0.08f, 0.02f, 0.02f));
                for (int x = 54; x <= 74; x++)
                {
                    float y = 76f - 0.025f * (x - 64) * (x - 64);
                    Dot(px, W, H, x, y, 1.1f, new Color(0.4f, 0.18f, 0.16f));
                }
            }
            return Make(W, H, anomalous ? "Portrait_Anomaly" : "Portrait_Normal", px, false);
        }

        public static Texture2D Landscape(int seed, bool figure = false)
        {
            const int W = 160, H = 120;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = (float)y / H;
                    Color c = Color.Lerp(new Color(0.35f, 0.3f, 0.25f), new Color(0.12f, 0.15f, 0.22f), v);
                    float hill1 = 45f + 10f * Mathf.Sin(x * 0.05f + seed) + 5f * Mathf.Sin(x * 0.13f);
                    float hill2 = 30f + 8f * Mathf.Sin(x * 0.07f + seed * 2f);
                    if (y < hill1) c = new Color(0.16f, 0.2f, 0.13f);
                    if (y < hill2) c = new Color(0.1f, 0.13f, 0.08f);
                    c *= 0.85f + 0.25f * Fbm((float)x / W, v, 4, seed + 5);
                    px[y * W + x] = Opaque(c);
                }
            Dot(px, W, H, 125, 95, 8, new Color(0.85f, 0.82f, 0.7f));
            // casa
            for (int y = 40; y < 58; y++)
                for (int x = 60; x < 84; x++) px[y * W + x] = new Color(0.08f, 0.07f, 0.07f);
            for (int y = 58; y < 70; y++)
                for (int x = 58; x < 86; x++)
                    if (Mathf.Abs(x - 72) < (70 - y) * 1.15f) px[y * W + x] = new Color(0.06f, 0.05f, 0.05f);
            for (int y = 46; y < 52; y++)
                for (int x = 66; x < 71; x++) px[y * W + x] = new Color(0.9f, 0.7f, 0.3f);
            if (figure)
            {
                Color pale = new Color(0.78f, 0.75f, 0.68f);
                FillEllipse(px, W, H, 96, 41, 2.6f, 9f, pale);
                Dot(px, W, H, 96, 53, 3f, pale);
                Line(px, W, H, 94, 46, 91, 36, 0.8f, pale);
                Line(px, W, H, 98, 46, 101, 36, 0.8f, pale);
            }
            return Make(W, H, (figure ? "LandscapeFigure_" : "Landscape_") + seed, px, false);
        }

        /// <summary>Cristal de espejo sucio. Con "hands" aparecen huellas de manos y regueros.</summary>
        public static Texture2D MirrorGlass(bool hands)
        {
            const int W = 128, H = 160;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    Color c = Color.Lerp(new Color(0.3f, 0.34f, 0.36f), new Color(0.55f, 0.6f, 0.62f), v);
                    float smudge = Fbm(u, v, 4, 91);
                    c *= 0.8f + 0.35f * smudge;
                    float ex = Mathf.Min(u, 1f - u), ey = Mathf.Min(v, 1f - v);
                    c *= Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Mathf.Min(ex, ey) * 8f));
                    px[y * W + x] = Opaque(c);
                }
            if (hands)
            {
                Color print = new Color(0.16f, 0.15f, 0.14f);
                DrawHand(px, W, H, 42, 92, 1f, print);
                DrawHand(px, W, H, 86, 108, 1.1f, print);
                Line(px, W, H, 40, 80, 39, 40, 1f, print);
                Line(px, W, H, 46, 80, 47, 52, 0.8f, print);
                Line(px, W, H, 86, 94, 85, 30, 1f, print);
            }
            return Make(W, H, hands ? "Mirror_Hands" : "Mirror_Normal", px, false);
        }

        static void DrawHand(Color[] px, int W, int H, float cx, float cy, float k, Color c)
        {
            FillEllipse(px, W, H, cx, cy, 9f * k, 11f * k, c);
            float[] ang = { -60f, -25f, -5f, 15f, 35f };
            float[] len = { 10f, 15f, 17f, 15f, 12f };
            for (int i = 0; i < 5; i++)
            {
                float a = ang[i] * Mathf.Deg2Rad;
                float bx = cx + Mathf.Sin(a) * 8f * k, by = cy + Mathf.Cos(a) * 9f * k;
                Line(px, W, H, bx, by, bx + Mathf.Sin(a) * len[i] * k, by + Mathf.Cos(a) * len[i] * k, 2.2f * k, c);
            }
        }

        /// <summary>Patrón de luz irregular para la linterna (cookie).</summary>
        public static Texture2D FlashlightCookie()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            t.name = "FlashlightCookie";
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Haz de linterna real: centro algo más intenso y caída larga y suave hacia el borde (sin aro duro).
                    float a = 1f - Mathf.SmoothStep(0.15f, 0.98f, r);
                    a = a * a * (3f - 2f * a);
                    a *= 0.92f + 0.04f * Mathf.Sin(r * 30f) + 0.04f * VNoise(x * 0.15f, y * 0.15f, 64, 7);
                    a += 0.15f * (1f - Mathf.SmoothStep(0f, 0.25f, r));
                    a = Mathf.Clamp01(a);
                    px[y * S + x] = new Color(a, a, a, a);
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
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

        public static Texture2D ClockFace()
        {
            const int W = 128, H = 128;
            var px = new Color[W * H];
            Color rim = new Color(0.2f, 0.12f, 0.07f);
            for (int i = 0; i < px.Length; i++) px[i] = rim;
            FillEllipse(px, W, H, 64, 64, 58, 58, new Color(0.15f, 0.13f, 0.11f));
            FillEllipse(px, W, H, 64, 64, 55, 55, new Color(0.88f, 0.84f, 0.74f));
            for (int k = 0; k < 12; k++)
            {
                float ang = k * Mathf.PI / 6f;
                float s = Mathf.Sin(ang), c = Mathf.Cos(ang);
                float r0 = (k % 3 == 0) ? 40f : 45f;
                Line(px, W, H, 64 + s * r0, 64 + c * r0, 64 + s * 51f, 64 + c * 51f, (k % 3 == 0) ? 2f : 1.2f, new Color(0.1f, 0.08f, 0.06f));
            }
            Dot(px, W, H, 64, 64, 3f, new Color(0.1f, 0.08f, 0.06f));
            return Make(W, H, "ClockFace", px, false);
        }
    }
}
