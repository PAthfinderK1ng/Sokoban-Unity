using System.Collections.Generic;

namespace Sokoban.Core
{
    /// <summary>
    /// Structural checks for a level (used by the editor before saving / play testing).
    /// Solvability is checked separately with <see cref="Solver"/>.
    /// </summary>
    public static class LevelValidator
    {
        public const int MinSize = 3;
        public const int MaxSize = 30;

        public static List<string> Validate(LevelData level)
        {
            var issues = new List<string>();
            if (level == null || level.rows == null || level.width < MinSize || level.height < MinSize)
            {
                issues.Add("关卡尺寸过小（至少 3x3）");
                return issues;
            }
            if (level.width > MaxSize || level.height > MaxSize)
                issues.Add("关卡尺寸过大（最大 " + MaxSize + "x" + MaxSize + "）");

            int players = level.CountPlayers();
            int boxes = level.CountBoxes();
            int goals = level.CountGoals();
            if (players == 0) issues.Add("缺少玩家起点");
            if (players > 1) issues.Add("玩家起点只能有 1 个（当前 " + players + " 个）");
            if (boxes == 0) issues.Add("至少需要 1 个箱子");
            if (goals == 0) issues.Add("至少需要 1 个目标点");
            if (boxes != goals) issues.Add("箱子数(" + boxes + ")与目标数(" + goals + ")不一致");
            if (issues.Count > 0) return issues;

            // Enclosure: the area the player can reach (boxes treated as passable) must not touch the border.
            var game = new SokobanGame(level);
            var start = game.PlayerPos;
            var seen = new bool[level.width * level.height];
            var q = new Queue<Int2>();
            q.Enqueue(start);
            seen[start.y * level.width + start.x] = true;
            bool leaks = false;
            var region = new List<Int2>();
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                region.Add(p);
                if (p.x == 0 || p.y == 0 || p.x == level.width - 1 || p.y == level.height - 1) leaks = true;
                foreach (var d in DirectionUtil.All)
                {
                    var np = p + DirectionUtil.Delta(d);
                    if (!game.InBounds(np) || level.Get(np.x, np.y) == Tiles.Wall) continue;
                    int i = np.y * level.width + np.x;
                    if (seen[i]) continue;
                    seen[i] = true;
                    q.Enqueue(np);
                }
            }
            if (leaks) issues.Add("玩家活动区域没有被墙完全围住");

            int boxesOutside = 0, goalsOutside = 0;
            for (int y = 0; y < level.height; y++)
                for (int x = 0; x < level.width; x++)
                {
                    char c = level.Get(x, y);
                    if (seen[y * level.width + x]) continue;
                    if (Tiles.HasBox(c)) boxesOutside++;
                    if (Tiles.HasGoal(c)) goalsOutside++;
                }
            if (boxesOutside > 0) issues.Add(boxesOutside + " 个箱子在玩家无法到达的区域");
            if (goalsOutside > 0) issues.Add(goalsOutside + " 个目标点在玩家无法到达的区域");

            if (issues.Count == 0 && game.IsSolved) issues.Add("所有箱子已经在目标点上，关卡无需操作");
            return issues;
        }

        /// <summary>Minimal checks needed so that the solver / game can run at all. Returns null when OK.</summary>
        public static string CheckStructure(LevelData level)
        {
            if (level == null || level.width <= 0 || level.height <= 0) return "empty level";
            if (level.CountPlayers() != 1) return "level must contain exactly one player";
            int b = level.CountBoxes(), g = level.CountGoals();
            if (b == 0) return "level has no boxes";
            if (b > g) return "more boxes than goals";
            return null;
        }
    }
}
