using System;
using System.Collections.Generic;
using System.Text;

namespace Sokoban.Core
{
    /// <summary>
    /// Standard Sokoban (XSB) tile characters.
    /// </summary>
    public static class Tiles
    {
        public const char Wall = '#';
        public const char Floor = ' ';
        public const char Goal = '.';
        public const char Box = '$';
        public const char BoxOnGoal = '*';
        public const char Player = '@';
        public const char PlayerOnGoal = '+';

        public static bool IsValid(char c)
        {
            return c == Wall || c == Floor || c == Goal || c == Box || c == BoxOnGoal
                   || c == Player || c == PlayerOnGoal || c == '-' || c == '_';
        }

        public static char Normalize(char c)
        {
            return (c == '-' || c == '_') ? Floor : c;
        }

        public static bool HasGoal(char c) { return c == Goal || c == BoxOnGoal || c == PlayerOnGoal; }
        public static bool HasBox(char c) { return c == Box || c == BoxOnGoal; }
        public static bool HasPlayer(char c) { return c == Player || c == PlayerOnGoal; }

        public static char Compose(bool wall, bool goal, bool box, bool player)
        {
            if (wall) return Wall;
            if (box) return goal ? BoxOnGoal : Box;
            if (player) return goal ? PlayerOnGoal : Player;
            return goal ? Goal : Floor;
        }
    }

    /// <summary>
    /// Immutable-ish level description: a rectangular grid of XSB characters plus metadata.
    /// </summary>
    [Serializable]
    public class LevelData
    {
        public string id = "";
        public string title = "";
        public string author = "";
        /// <summary>Reference solution length in moves (0 = unknown). Used for star rating.</summary>
        public int par;
        public int width;
        public int height;
        public string[] rows = new string[0];

        public LevelData() { }

        public LevelData(int width, int height)
        {
            this.width = width;
            this.height = height;
            rows = new string[height];
            for (int y = 0; y < height; y++) rows[y] = new string(Tiles.Floor, width);
        }

        public char Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return Tiles.Floor;
            return rows[y][x];
        }

        public void Set(int x, int y, char c)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            char[] arr = rows[y].ToCharArray();
            arr[x] = c;
            rows[y] = new string(arr);
        }

        public LevelData Clone()
        {
            var l = new LevelData
            {
                id = id, title = title, author = author, par = par,
                width = width, height = height, rows = (string[])rows.Clone()
            };
            return l;
        }

        /// <summary>Resize keeping content anchored at top-left.</summary>
        public void Resize(int newW, int newH)
        {
            var newRows = new string[newH];
            for (int y = 0; y < newH; y++)
            {
                var sb = new StringBuilder(newW);
                for (int x = 0; x < newW; x++) sb.Append(Get(x, y));
                newRows[y] = sb.ToString();
            }
            width = newW;
            height = newH;
            rows = newRows;
        }

        /// <summary>Shift the whole content by (dx,dy); cells shifted out are lost.</summary>
        public void Shift(int dx, int dy)
        {
            var copy = Clone();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Set(x, y, copy.Get(x - dx, y - dy));
        }

        public int CountBoxes()
        {
            int n = 0;
            foreach (var r in rows) foreach (var c in r) if (Tiles.HasBox(c)) n++;
            return n;
        }

        public int CountGoals()
        {
            int n = 0;
            foreach (var r in rows) foreach (var c in r) if (Tiles.HasGoal(c)) n++;
            return n;
        }

        public int CountPlayers()
        {
            int n = 0;
            foreach (var r in rows) foreach (var c in r) if (Tiles.HasPlayer(c)) n++;
            return n;
        }

        /// <summary>Content equality (ignores metadata).</summary>
        public bool SameLayout(LevelData other)
        {
            if (other == null || other.width != width || other.height != height) return false;
            for (int y = 0; y < height; y++) if (rows[y] != other.rows[y]) return false;
            return true;
        }

        /// <summary>
        /// Remove fully empty outer rows / columns (everything that is not a wall or object).
        /// </summary>
        public void Trim()
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (rows[y][x] != Tiles.Floor)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return;
            int w = maxX - minX + 1, h = maxY - minY + 1;
            var newRows = new string[h];
            for (int y = 0; y < h; y++) newRows[y] = rows[y + minY].Substring(minX, w);
            rows = newRows;
            width = w;
            height = h;
        }

        public string ToXsb(bool includeMetadata)
        {
            var sb = new StringBuilder();
            if (includeMetadata)
            {
                if (!string.IsNullOrEmpty(title)) sb.Append("Title: ").Append(title).Append('\n');
                if (!string.IsNullOrEmpty(author)) sb.Append("Author: ").Append(author).Append('\n');
                if (!string.IsNullOrEmpty(id)) sb.Append("Id: ").Append(id).Append('\n');
                if (par > 0) sb.Append("Par: ").Append(par).Append('\n');
            }
            foreach (var r in rows) sb.Append(r.TrimEnd()).Append('\n');
            return sb.ToString();
        }

        public override string ToString() { return ToXsb(true); }
    }

    /// <summary>
    /// Parses level collections in the common text format:
    /// <code>
    /// ; comment
    /// Title: My level
    /// Par: 42
    /// #####
    /// #@$.#
    /// #####
    /// </code>
    /// Levels are separated by blank lines. Metadata lines (Key: value) may appear before or after the map.
    /// </summary>
    public static class LevelParser
    {
        public static List<LevelData> ParseCollection(string text)
        {
            var result = new List<LevelData>();
            if (string.IsNullOrEmpty(text)) return result;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            var lines = text.Split('\n');

            var mapLines = new List<string>();
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool mapClosed = false;

            Action flush = () =>
            {
                if (mapLines.Count > 0)
                {
                    var lvl = FromRows(mapLines);
                    ApplyMeta(lvl, meta);
                    result.Add(lvl);
                }
                mapLines.Clear();
                meta.Clear();
                mapClosed = false;
            };

            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    if (mapLines.Count > 0) mapClosed = true;
                    continue;
                }
                if (line.TrimStart().StartsWith(";")) continue;

                if (IsMapLine(line))
                {
                    if (mapClosed) flush();
                    mapLines.Add(line);
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    // Metadata after a finished map belongs to that map unless it is a new Title.
                    string key = line.Substring(0, colon).Trim();
                    string val = line.Substring(colon + 1).Trim();
                    // After a finished map, a new Title starts the next level; other keys still belong to the finished map.
                    if (mapClosed && key.Equals("Title", StringComparison.OrdinalIgnoreCase)) flush();
                    meta[key] = val;
                }
            }
            flush();
            return result;
        }

        public static LevelData ParseSingle(string text)
        {
            var list = ParseCollection(text);
            return list.Count > 0 ? list[0] : null;
        }

        private static bool IsMapLine(string line)
        {
            bool hasWallOrObject = false;
            foreach (char c in line)
            {
                if (!Tiles.IsValid(c)) return false;
                if (c != ' ') hasWallOrObject = true;
            }
            return hasWallOrObject;
        }

        private static LevelData FromRows(List<string> lines)
        {
            int w = 0;
            foreach (var l in lines) if (l.Length > w) w = l.Length;
            var lvl = new LevelData { width = w, height = lines.Count, rows = new string[lines.Count] };
            for (int y = 0; y < lines.Count; y++)
            {
                var sb = new StringBuilder(w);
                foreach (char c in lines[y]) sb.Append(Tiles.Normalize(c));
                while (sb.Length < w) sb.Append(Tiles.Floor);
                lvl.rows[y] = sb.ToString();
            }
            return lvl;
        }

        private static void ApplyMeta(LevelData lvl, Dictionary<string, string> meta)
        {
            string v;
            if (meta.TryGetValue("Title", out v)) lvl.title = v;
            if (meta.TryGetValue("Author", out v)) lvl.author = v;
            if (meta.TryGetValue("Id", out v)) lvl.id = v;
            int par;
            if (meta.TryGetValue("Par", out v) && int.TryParse(v, out par)) lvl.par = par;
        }

        public static string SerializeCollection(IEnumerable<LevelData> levels)
        {
            var sb = new StringBuilder();
            foreach (var l in levels)
            {
                sb.Append(l.ToXsb(true));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
