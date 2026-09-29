using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public class GeneratorOptions
    {
        public int width = 9;          // outer size including walls
        public int height = 8;
        public int boxes = 3;
        public float wallDensity = 0.18f;
        public int pullSteps = 300;
        public int attempts = 30;      // candidates generated; the hardest solvable one is returned
        public int solverNodes = 150000;
        public int seed;
        public Func<bool> isCancelled;
    }

    /// <summary>
    /// Procedural level generator using the "reverse play" technique:
    /// start from the solved position and randomly *pull* boxes away from goals, which guarantees
    /// the result is solvable. Candidates are then scored with the solver (push-optimal length).
    /// </summary>
    public static class LevelGenerator
    {
        public static LevelData Generate(GeneratorOptions o, out SolveResult bestResult)
        {
            var rng = new Random(o.seed);
            LevelData best = null;
            bestResult = null;
            int bestScore = -1;
            for (int a = 0; a < o.attempts; a++)
            {
                if (o.isCancelled != null && o.isCancelled()) break;
                var lvl = TryCreate(o, rng);
                if (lvl == null) continue;
                var res = Solver.Solve(lvl, new SolverOptions { maxNodes = o.solverNodes, optimal = true, isCancelled = o.isCancelled });
                if (res.status != SolveStatus.Solved || res.pushes == 0) continue;
                int score = Score(lvl, res);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = lvl;
                    bestResult = res;
                }
            }
            if (best != null) best.par = bestResult.moves;
            return best;
        }

        /// <summary>Difficulty estimate: pushes, plus bonus for direction changes of pushes and box count.</summary>
        public static int Score(LevelData lvl, SolveResult res)
        {
            int turns = 0;
            char prev = '\0';
            foreach (char c in res.solution)
            {
                if (!char.IsUpper(c)) continue;
                if (prev != '\0' && c != prev) turns++;
                prev = c;
            }
            return res.pushes * 2 + turns * 3 + res.moves / 4;
        }

        private static LevelData TryCreate(GeneratorOptions o, Random rng)
        {
            int w = Math.Max(5, o.width), h = Math.Max(5, o.height);
            var wall = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    wall[y * w + x] = x == 0 || y == 0 || x == w - 1 || y == h - 1;

            // random interior walls, as small 1..2 cell blobs
            int interior = (w - 2) * (h - 2);
            int target = (int)(interior * o.wallDensity);
            int placed = 0, guard = 0;
            while (placed < target && guard++ < interior * 10)
            {
                int x = rng.Next(1, w - 1), y = rng.Next(1, h - 1);
                int i = y * w + x;
                if (wall[i]) continue;
                wall[i] = true;
                if (!FloorConnected(wall, w, h)) { wall[i] = false; continue; }
                placed++;
                if (rng.NextDouble() < 0.35)
                {
                    var d = DirectionUtil.Delta(DirectionUtil.All[rng.Next(4)]);
                    int x2 = x + d.x, y2 = y + d.y;
                    if (x2 > 0 && y2 > 0 && x2 < w - 1 && y2 < h - 1 && !wall[y2 * w + x2])
                    {
                        wall[y2 * w + x2] = true;
                        if (!FloorConnected(wall, w, h)) wall[y2 * w + x2] = false; else placed++;
                    }
                }
            }

            var floor = new List<int>();
            for (int i = 0; i < wall.Length; i++) if (!wall[i]) floor.Add(i);
            if (floor.Count < o.boxes * 3 + 4) return null;

            // goals: avoid cells with >= 3 wall neighbours (dead ends make boring goals)
            var box = new bool[w * h];
            var goal = new bool[w * h];
            var boxList = new List<int>();
            guard = 0;
            while (boxList.Count < o.boxes && guard++ < 500)
            {
                int c = floor[rng.Next(floor.Count)];
                if (goal[c]) continue;
                if (WallNeighbours(wall, w, c) >= 3) continue;
                goal[c] = true;
                box[c] = true;
                boxList.Add(c);
            }
            if (boxList.Count < o.boxes) return null;

            int player;
            guard = 0;
            do { player = floor[rng.Next(floor.Count)]; } while (box[player] && guard++ < 100);
            if (box[player]) return null;

            // reverse play: random walk with pulls
            int[] off = { -w, w, -1, 1 };
            for (int s = 0; s < o.pullSteps; s++)
            {
                int d = rng.Next(4);
                int next = player + off[d];
                if (wall[next] || box[next]) continue;
                int behind = player - off[d];
                bool pull = box[behind] && rng.NextDouble() < 0.6;
                if (pull)
                {
                    box[behind] = false;
                    box[player] = true;
                }
                player = next;
            }

            int onGoal = 0;
            for (int i = 0; i < box.Length; i++) if (box[i] && goal[i]) onGoal++;
            if (onGoal == o.boxes) return null;

            // Turn floor unreachable by the player (boxes passable) into wall
            var reach = new bool[w * h];
            var q = new Queue<int>();
            q.Enqueue(player);
            reach[player] = true;
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int n = c + off[d];
                    if (wall[n] || reach[n]) continue;
                    reach[n] = true;
                    q.Enqueue(n);
                }
            }

            var lvl = new LevelData(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    bool isWall = wall[i] || !reach[i];
                    char ch = Tiles.Compose(isWall, goal[i] && !isWall, box[i] && !isWall, i == player);
                    lvl.Set(x, y, ch);
                }
            RemoveHiddenWalls(lvl);
            lvl.Trim();
            return lvl;
        }

        /// <summary>Walls that do not touch any floor cell (incl. diagonals) are purely decorative -> remove.</summary>
        public static void RemoveHiddenWalls(LevelData lvl)
        {
            var copy = lvl.Clone();
            for (int y = 0; y < lvl.height; y++)
                for (int x = 0; x < lvl.width; x++)
                {
                    if (copy.Get(x, y) != Tiles.Wall) continue;
                    bool touches = false;
                    for (int dy = -1; dy <= 1 && !touches; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= lvl.width || ny >= lvl.height) continue;
                            if (copy.Get(nx, ny) != Tiles.Wall) { touches = true; break; }
                        }
                    if (!touches) lvl.Set(x, y, Tiles.Floor);
                }
        }

        private static int WallNeighbours(bool[] wall, int w, int c)
        {
            int n = 0;
            if (wall[c - w]) n++;
            if (wall[c + w]) n++;
            if (wall[c - 1]) n++;
            if (wall[c + 1]) n++;
            return n;
        }

        private static bool FloorConnected(bool[] wall, int w, int h)
        {
            int start = -1, total = 0;
            for (int i = 0; i < wall.Length; i++)
                if (!wall[i]) { total++; if (start < 0) start = i; }
            if (start < 0) return false;
            var seen = new bool[wall.Length];
            var q = new Queue<int>();
            q.Enqueue(start);
            seen[start] = true;
            int count = 0;
            int[] off = { -w, w, -1, 1 };
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                count++;
                foreach (int o in off)
                {
                    int n = c + o;
                    if (n < 0 || n >= wall.Length || wall[n] || seen[n]) continue;
                    seen[n] = true;
                    q.Enqueue(n);
                }
            }
            return count == total;
        }
    }
}
