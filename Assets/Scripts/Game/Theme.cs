using UnityEngine;

namespace Sokoban.Game
{
    /// <summary>Central palette. All art is procedural placeholder art built from these colours.</summary>
    public static class Theme
    {
        public static Color Hex(string hex)
        {
            Color c;
            ColorUtility.TryParseHtmlString(hex, out c);
            return c;
        }

        // World
        public static readonly Color Background = Hex("#1B2030");
        public static readonly Color FloorA = Hex("#3A3F55");
        public static readonly Color FloorB = Hex("#363B50");
        public static readonly Color Wall = Hex("#7A5C4A");
        public static readonly Color WallDark = Hex("#5A4033");
        public static readonly Color WallLight = Hex("#9C7A63");
        public static readonly Color Goal = Hex("#F2C14E");
        public static readonly Color Box = Hex("#D08C4A");
        public static readonly Color BoxDark = Hex("#8E5A2B");
        public static readonly Color BoxDone = Hex("#5FBF77");
        public static readonly Color BoxDoneDark = Hex("#2F7D46");
        public static readonly Color Player = Hex("#4FA3F7");
        public static readonly Color PlayerDark = Hex("#2468B0");
        public static readonly Color EditorEmpty = Hex("#242A3B");
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.07f);

        // UI
        public static readonly Color PanelBg = Hex("#252B3DEE");
        public static readonly Color PanelBgSolid = Hex("#252B3D");
        public static readonly Color PanelLight = Hex("#323A52");
        public static readonly Color Overlay = new Color(0.05f, 0.06f, 0.1f, 0.72f);
        public static readonly Color Text = Hex("#EDEFF5");
        public static readonly Color TextDim = Hex("#9AA3BD");
        public static readonly Color Accent = Hex("#4FA3F7");
        public static readonly Color AccentGreen = Hex("#4CB86A");
        public static readonly Color AccentYellow = Hex("#F2C14E");
        public static readonly Color Danger = Hex("#E0605E");
        public static readonly Color Button = Hex("#3B4561");
        public static readonly Color ButtonSelected = Hex("#4FA3F7");
        public static readonly Color Locked = Hex("#2A3044");
    }
}
