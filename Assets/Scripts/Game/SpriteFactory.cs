using UnityEngine;

namespace Sokoban.Game
{
    /// <summary>
    /// Procedurally generated placeholder art (no external assets needed).
    /// Every sprite is 64x64 px with 64 pixels per unit, so one grid cell == one world unit.
    /// </summary>
    public class SpriteFactory
    {
        public const int Size = 64;

        public Sprite FloorA, FloorB, Wall, Goal, Box, BoxOnGoal, EditorCell, Highlight, Shadow;
        public Sprite[] Player = new Sprite[4]; // Up, Down, Left, Right (same order as Direction)

        // UI
        public Sprite Rounded, RoundedOutline, Circle, White;

        public SpriteFactory()
        {
            FloorA = Make("floorA", (x, y) => Tile(x, y, Theme.FloorA, 0.04f));
            FloorB = Make("floorB", (x, y) => Tile(x, y, Theme.FloorB, 0.04f));
            Wall = Make("wall", WallPixel);
            Goal = Make("goal", GoalPixel);
            Box = Make("box", (x, y) => BoxPixel(x, y, Theme.Box, Theme.BoxDark));
            BoxOnGoal = Make("boxGoal", (x, y) => BoxPixel(x, y, Theme.BoxDone, Theme.BoxDoneDark));
            EditorCell = Make("editorCell", (x, y) =>
            {
                float d = RoundRect(x, y, 32, 32, 31, 31, 2);
                return d > -1.2f ? Theme.GridLine : Color.clear;
            });
            Highlight = Make("highlight", (x, y) =>
            {
                float d = RoundRect(x, y, 32, 32, 30, 30, 8);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d + 2f) / 2.2f);
                return new Color(1f, 1f, 1f, a);
            });
            Shadow = Make("shadow", (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(32, 26)) / 26f;
                return new Color(0, 0, 0, Mathf.Clamp01(1 - d) * 0.35f);
            });
            for (int i = 0; i < 4; i++)
            {
                int dir = i;
                Player[i] = Make("player" + i, (x, y) => PlayerPixel(x, y, dir));
            }

            White = Make("white", (x, y) => Color.white);
            Circle = Make("circle", (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) - 31f;
                return new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
            });
            Rounded = MakeSliced("rounded", (x, y) =>
            {
                float d = RoundRect(x, y, 32, 32, 32, 32, 16);
                return new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
            }, 20);
            RoundedOutline = MakeSliced("roundedOutline", (x, y) =>
            {
                float d = RoundRect(x, y, 32, 32, 32, 32, 16);
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + 4.5f);
                return new Color(1, 1, 1, a);
            }, 20);
        }

        // ----------------------------------------------------------------- pixel shaders

        private delegate Color PixelFn(int x, int y);

        private static Sprite Make(string name, PixelFn fn)
        {
            var tex = Bake(name, fn);
            var s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        private static Sprite MakeSliced(string name, PixelFn fn, int border)
        {
            var tex = Bake(name, fn);
            var s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s.name = name;
            return s;
        }

        private static Texture2D Bake(string name, PixelFn fn)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[y * Size + x] = fn(x, y);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Signed distance to a rounded rectangle (negative inside).</summary>
        private static float RoundRect(float x, float y, float cx, float cy, float hw, float hh, float r)
        {
            float px = Mathf.Abs(x + 0.5f - cx) - (hw - r);
            float py = Mathf.Abs(y + 0.5f - cy) - (hh - r);
            float ox = Mathf.Max(px, 0), oy = Mathf.Max(py, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(px, py), 0) - r;
        }

        private static Color Over(Color dst, Color src, float a)
        {
            a = Mathf.Clamp01(a) * src.a;
            return new Color(
                Mathf.Lerp(dst.r, src.r, a),
                Mathf.Lerp(dst.g, src.g, a),
                Mathf.Lerp(dst.b, src.b, a),
                Mathf.Max(dst.a, a));
        }

        private static Color Tile(int x, int y, Color c, float variation)
        {
            // full square tile with a soft inner bevel
            float d = RoundRect(x, y, 32, 32, 32, 32, 3);
            Color col = c;
            float edge = Mathf.Clamp01((d + 3f) / 3f);
            col = Color.Lerp(col, col * 0.85f, edge);
            float noise = (Mathf.PerlinNoise(x * 0.15f, y * 0.15f) - 0.5f) * variation;
            col.r += noise; col.g += noise; col.b += noise;
            col.a = 1f;
            return col;
        }

        private static Color WallPixel(int x, int y)
        {
            // bricks: 4 rows of 16px, offset every other row
            int row = y / 16;
            int offset = (row % 2) * 16;
            int bx = (x + offset) % 32;
            int by = y % 16;
            bool mortar = by < 2 || bx < 2;
            Color c = mortar ? Theme.WallDark : Theme.Wall;
            if (!mortar)
            {
                if (by >= 13) c = Color.Lerp(c, Theme.WallLight, 0.6f); // top highlight (y up)
                if (by <= 3) c = Color.Lerp(c, Theme.WallDark, 0.4f);
                float n = (Mathf.PerlinNoise((x + row * 7) * 0.3f, y * 0.3f) - 0.5f) * 0.08f;
                c.r += n; c.g += n; c.b += n;
            }
            c.a = 1;
            return c;
        }

        private static Color GoalPixel(int x, int y)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32));
            Color c = Color.clear;
            // soft glow
            float glow = Mathf.Clamp01(1f - d / 26f) * 0.25f;
            c = Over(c, Theme.Goal, glow);
            // ring
            float ring = Mathf.Clamp01(2.2f - Mathf.Abs(d - 15f));
            c = Over(c, Theme.Goal, ring);
            // dot
            c = Over(c, Theme.Goal, Mathf.Clamp01(5.5f - d));
            return c;
        }

        private static Color BoxPixel(int x, int y, Color main, Color dark)
        {
            Color c = Color.clear;
            float outer = RoundRect(x, y, 32, 32, 27, 27, 6);
            if (outer > 1f) return c;
            c = Over(c, dark, Mathf.Clamp01(0.5f - outer));
            float inner = RoundRect(x, y, 32, 32, 22, 22, 3);
            Color wood = main;
            // plank stripes
            float stripe = Mathf.Sin((y) * 0.55f) * 0.04f;
            wood.r += stripe; wood.g += stripe; wood.b += stripe;
            c = Over(c, wood, Mathf.Clamp01(0.5f - inner));
            // diagonal braces
            float diag1 = Mathf.Abs((x - 32) - (y - 32)) / 1.414f;
            float diag2 = Mathf.Abs((x - 32) + (y - 32)) / 1.414f;
            if (inner < 0)
            {
                c = Over(c, dark, Mathf.Clamp01(3.2f - diag1) * 0.9f);
                c = Over(c, dark, Mathf.Clamp01(3.2f - diag2) * 0.9f);
            }
            // top highlight
            if (y > 52 && outer < 0) c = Over(c, Color.white, 0.12f);
            return c;
        }

        private static Color PlayerPixel(int x, int y, int dir)
        {
            Color c = Color.clear;
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            Vector2 center = new Vector2(32, 31);
            float d = Vector2.Distance(p, center);
            // outline + body
            c = Over(c, Theme.PlayerDark, Mathf.Clamp01(24.5f - d));
            c = Over(c, Theme.Player, Mathf.Clamp01(21.5f - d));
            // shine
            float s = Vector2.Distance(p, center + new Vector2(-7, 8));
            c = Over(c, Color.white, Mathf.Clamp01(6f - s) * 0.35f);

            // eyes looking towards direction (Unity texture y is up)
            Vector2 look;
            switch (dir)
            {
                case 0: look = new Vector2(0, 5); break;   // up
                case 1: look = new Vector2(0, -3); break;  // down
                case 2: look = new Vector2(-6, 1); break;  // left
                default: look = new Vector2(6, 1); break;  // right
            }
            Vector2 eyeL = center + new Vector2(-7, 3) + look;
            Vector2 eyeR = center + new Vector2(7, 3) + look;
            if (dir == 2) eyeR.x -= 3;
            if (dir == 3) eyeL.x += 3;
            {
                c = Over(c, Color.white, Mathf.Clamp01(5.2f - Vector2.Distance(p, eyeL)));
                c = Over(c, Color.white, Mathf.Clamp01(5.2f - Vector2.Distance(p, eyeR)));
                Vector2 pupil = look.normalized * 1.8f;
                c = Over(c, Theme.Background, Mathf.Clamp01(2.8f - Vector2.Distance(p, eyeL + pupil)));
                c = Over(c, Theme.Background, Mathf.Clamp01(2.8f - Vector2.Distance(p, eyeR + pupil)));
            }
            return c;
        }
    }
}
