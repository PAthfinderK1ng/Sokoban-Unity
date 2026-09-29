using System;
using System.Collections.Generic;
using System.Text;

namespace Sokoban.Core
{
    public enum SolveStatus
    {
        Solved,
        Unsolvable,
        LimitReached,
        Cancelled,
        Invalid
    }

    public class SolveResult
    {
        public SolveStatus status;
        /// <summary>Full LURD solution (lower case walk, upper case push).</summary>
        public string solution = "";
        public int pushes;
        public int moves;
        public int nodes;
        public string message = "";

        public override string ToString()
        {
            return status + " moves=" + moves + " pushes=" + pushes + " nodes=" + nodes + (message.Length > 0 ? " (" + message + ")" : "");
        }
    }

    public class SolverOptions
    {
        /// <summary>Maximum number of distinct states to expand.</summary>
        public int maxNodes = 400000;
        /// <summary>true = breadth first (push-optimal); false = greedy best-first (fast, not optimal).</summary>
        public bool optimal = true;
        /// <summary>Polled regularly; return true to abort.</summary>
        public Func<bool> isCancelled;
    }

    /// <summary>
    /// Push-based Sokoban solver.
    /// State = box positions + normalized player region. Pruning: dead squares (reverse-pull analysis)
    /// and 2x2 freeze deadlocks. Runs off the main thread in Unity (pure C#).
    /// </summary>
    public class Solver
    {
        private int _w, _h, _n;
        private bool[] _wall;
        private bool[] _goal;
        private bool[] _live;
        private int[] _goalList;
        private int[] _dirOffset;
        private ulong[] _zBox;
        private ulong[] _zPlayer;
        private int[] _goalDist; // min distance (ignoring boxes) from each cell to nearest goal, for heuristic

        private class Node
        {
            public int parent;
            public int boxFrom;   // cell index the pushed box came from
            public int dir;       // push direction
            public int player;    // player position after the push
            public int[] boxes;   // sorted
            public int g;
        }

        public static SolveResult Solve(LevelData level, SolverOptions options)
        {
            return new Solver().Run(level, options ?? new SolverOptions());
        }

        /// <summary>Try an optimal search first, fall back to a greedy search for large levels.</summary>
        public static SolveResult SolveAuto(LevelData level, int maxNodes, Func<bool> isCancelled)
        {
            var opt = Solve(level, new SolverOptions { maxNodes = maxNodes / 2, optimal = true, isCancelled = isCancelled });
            if (opt.status != SolveStatus.LimitReached) return opt;
            var greedy = Solve(level, new SolverOptions { maxNodes = maxNodes, optimal = false, isCancelled = isCancelled });
            greedy.nodes += opt.nodes;
            return greedy;
        }

        private SolveResult Run(LevelData level, SolverOptions opt)
        {
            var res = new SolveResult();
            var err = LevelValidator.CheckStructure(level);
            if (err != null)
            {
                res.status = SolveStatus.Invalid;
                res.message = err;
                return res;
            }

            _w = level.width;
            _h = level.height;
            _n = _w * _h;
            _wall = new bool[_n];
            _goal = new bool[_n];
            var boxes = new List<int>();
            int player = -1;
            var goals = new List<int>();
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    char c = level.Get(x, y);
                    int i = y * _w + x;
                    _wall[i] = c == Tiles.Wall;
                    _goal[i] = Tiles.HasGoal(c);
                    if (_goal[i]) goals.Add(i);
                    if (Tiles.HasBox(c)) boxes.Add(i);
                    if (Tiles.HasPlayer(c)) player = i;
                }
            _goalList = goals.ToArray();
            // Border cells can never hold a box that is pushed further; treat outside as wall.
            _dirOffset = new[] { -_w, _w, -1, 1 };

            ComputeLiveSquares();
            ComputeGoalDistances();
            InitZobrist();

            var startBoxes = boxes.ToArray();
            Array.Sort(startBoxes);
            if (AllOnGoal(startBoxes))
            {
                res.status = SolveStatus.Solved;
                return res;
            }
            foreach (var b in startBoxes)
            {
                if (!_live[b])
                {
                    res.status = SolveStatus.Unsolvable;
                    res.message = "box on dead square";
                    return res;
                }
            }

            var nodes = new List<Node>();
            var visited = new HashSet<ulong>();
            var boxGrid = new bool[_n];
            var reach = new int[_n];
            var reach2 = new int[_n];
            int reachStamp = 0, reach2Stamp = 0;
            var bfsQueue = new int[_n];

            var start = new Node { parent = -1, boxFrom = -1, dir = -1, player = player, boxes = startBoxes, g = 0 };
            nodes.Add(start);

            // open list: queue for BFS, heap for best-first
            var fifo = new Queue<int>();
            var heap = new MinHeap();
            if (opt.optimal) fifo.Enqueue(0); else heap.Push(0, Heuristic(startBoxes));

            // visited key needs normalized player; compute once for start
            reachStamp++;
            SetGrid(boxGrid, startBoxes, true);
            int norm = FloodFillMin(player, boxGrid, reach, reachStamp, bfsQueue);
            SetGrid(boxGrid, startBoxes, false);
            visited.Add(Hash(startBoxes, norm));

            int expanded = 0;
            while (opt.optimal ? fifo.Count > 0 : heap.Count > 0)
            {
                if ((expanded & 1023) == 0 && opt.isCancelled != null && opt.isCancelled())
                {
                    res.status = SolveStatus.Cancelled;
                    res.nodes = expanded;
                    return res;
                }
                if (expanded >= opt.maxNodes)
                {
                    res.status = SolveStatus.LimitReached;
                    res.nodes = expanded;
                    res.message = "search limit reached";
                    return res;
                }

                int ni = opt.optimal ? fifo.Dequeue() : heap.Pop();
                var node = nodes[ni];
                expanded++;

                SetGrid(boxGrid, node.boxes, true);
                reachStamp++;
                FloodFillMin(node.player, boxGrid, reach, reachStamp, bfsQueue);

                for (int bi = 0; bi < node.boxes.Length; bi++)
                {
                    int b = node.boxes[bi];
                    for (int d = 0; d < 4; d++)
                    {
                        int stand = b - _dirOffset[d];
                        int target = b + _dirOffset[d];
                        if (!Valid(b, d, -1) || !Valid(b, d, 1)) continue;
                        if (reach[stand] != reachStamp) continue;
                        if (_wall[target] || boxGrid[target] || !_live[target]) continue;

                        // apply push on grid for deadlock test
                        boxGrid[b] = false;
                        boxGrid[target] = true;
                        bool dead = IsFreezeDeadlock(target, boxGrid);
                        boxGrid[target] = false;
                        boxGrid[b] = true;
                        if (dead) continue;

                        var nb = (int[])node.boxes.Clone();
                        nb[bi] = target;
                        Array.Sort(nb);

                        // normalized player region after push
                        boxGrid[b] = false;
                        boxGrid[target] = true;
                        int pn = FloodFillMin(b, boxGrid, reach2, ++reach2Stamp, bfsQueue);
                        boxGrid[target] = false;
                        boxGrid[b] = true;

                        ulong h = Hash(nb, pn);
                        if (!visited.Add(h)) continue;

                        var child = new Node { parent = ni, boxFrom = b, dir = d, player = b, boxes = nb, g = node.g + 1 };
                        nodes.Add(child);
                        int ci = nodes.Count - 1;

                        if (AllOnGoal(nb))
                        {
                            SetGrid(boxGrid, node.boxes, false);
                            return Reconstruct(level, nodes, ci, expanded);
                        }
                        if (opt.optimal) fifo.Enqueue(ci);
                        else heap.Push(ci, Heuristic(nb) * 3 + child.g);
                    }
                }
                SetGrid(boxGrid, node.boxes, false);
            }

            res.status = SolveStatus.Unsolvable;
            res.nodes = expanded;
            return res;
        }

        // ------------------------------------------------------------------ helpers

        private bool Valid(int cell, int d, int k)
        {
            // make sure cell + k*offset stays inside the grid and does not wrap across rows
            int x = cell % _w, y = cell / _w;
            switch (d)
            {
                case 0: y -= k; break;
                case 1: y += k; break;
                case 2: x -= k; break;
                default: x += k; break;
            }
            return x >= 0 && y >= 0 && x < _w && y < _h;
        }

        private bool Neighbour(int cell, int d, out int result)
        {
            result = -1;
            if (!Valid(cell, d, 1)) return false;
            result = cell + _dirOffset[d];
            return true;
        }

        private static void SetGrid(bool[] grid, int[] boxes, bool v)
        {
            for (int i = 0; i < boxes.Length; i++) grid[boxes[i]] = v;
        }

        private bool AllOnGoal(int[] boxes)
        {
            for (int i = 0; i < boxes.Length; i++) if (!_goal[boxes[i]]) return false;
            return true;
        }

        /// <summary>Flood fill reachable cells, marking reach[] with stamp. Returns minimum reachable index.</summary>
        private int FloodFillMin(int from, bool[] boxGrid, int[] reach, int stamp, int[] queue)
        {
            int head = 0, tail = 0, min = from;
            queue[tail++] = from;
            reach[from] = stamp;
            while (head < tail)
            {
                int c = queue[head++];
                if (c < min) min = c;
                for (int d = 0; d < 4; d++)
                {
                    int nc;
                    if (!Neighbour(c, d, out nc)) continue;
                    if (reach[nc] == stamp || _wall[nc] || boxGrid[nc]) continue;
                    reach[nc] = stamp;
                    queue[tail++] = nc;
                }
            }
            return min;
        }

        private void ComputeLiveSquares()
        {
            // A square is live if a box on it can be pushed to some goal (ignoring other boxes).
            // Equivalent: reachable from a goal by "pulling": box at c pulled to c+d needs c+d and c+2d free.
            _live = new bool[_n];
            var q = new Queue<int>();
            foreach (var g in _goalList)
            {
                if (_wall[g]) continue;
                _live[g] = true;
                q.Enqueue(g);
            }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int c1, c2;
                    if (!Neighbour(c, d, out c1) || !Neighbour(c1, d, out c2)) continue;
                    if (_wall[c1] || _wall[c2] || _live[c1]) continue;
                    _live[c1] = true;
                    q.Enqueue(c1);
                }
            }
        }

        private void ComputeGoalDistances()
        {
            _goalDist = new int[_n];
            for (int i = 0; i < _n; i++) _goalDist[i] = int.MaxValue / 4;
            var q = new Queue<int>();
            foreach (var g in _goalList) { _goalDist[g] = 0; q.Enqueue(g); }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int nc;
                    if (!Neighbour(c, d, out nc) || _wall[nc]) continue;
                    if (_goalDist[nc] <= _goalDist[c] + 1) continue;
                    _goalDist[nc] = _goalDist[c] + 1;
                    q.Enqueue(nc);
                }
            }
        }

        private int Heuristic(int[] boxes)
        {
            int h = 0;
            for (int i = 0; i < boxes.Length; i++) h += _goalDist[boxes[i]];
            return h;
        }

        private bool Blocked(int cell, bool[] boxGrid)
        {
            return cell < 0 || _wall[cell] || boxGrid[cell];
        }

        /// <summary>Detects 2x2 blocks of walls/boxes containing the moved box where some box is off goal.</summary>
        private bool IsFreezeDeadlock(int cell, bool[] boxGrid)
        {
            int x = cell % _w, y = cell / _w;
            for (int ox = -1; ox <= 0; ox++)
            {
                for (int oy = -1; oy <= 0; oy++)
                {
                    int x0 = x + ox, y0 = y + oy;
                    if (x0 < 0 || y0 < 0 || x0 + 1 >= _w || y0 + 1 >= _h) continue;
                    int a = y0 * _w + x0, b = a + 1, c = a + _w, d = c + 1;
                    if (!Blocked(a, boxGrid) || !Blocked(b, boxGrid) || !Blocked(c, boxGrid) || !Blocked(d, boxGrid)) continue;
                    bool offGoal = (boxGrid[a] && !_goal[a]) || (boxGrid[b] && !_goal[b]) ||
                                   (boxGrid[c] && !_goal[c]) || (boxGrid[d] && !_goal[d]);
                    if (offGoal) return true;
                }
            }
            return false;
        }

        private void InitZobrist()
        {
            var rng = new Random(12345);
            _zBox = new ulong[_n];
            _zPlayer = new ulong[_n];
            var buf = new byte[8];
            for (int i = 0; i < _n; i++)
            {
                rng.NextBytes(buf); _zBox[i] = BitConverter.ToUInt64(buf, 0);
                rng.NextBytes(buf); _zPlayer[i] = BitConverter.ToUInt64(buf, 0);
            }
        }

        private ulong Hash(int[] boxes, int playerNorm)
        {
            ulong h = _zPlayer[playerNorm];
            for (int i = 0; i < boxes.Length; i++) h ^= _zBox[boxes[i]];
            return h;
        }

        private SolveResult Reconstruct(LevelData level, List<Node> nodes, int goalNode, int expanded)
        {
            var chain = new List<Node>();
            for (int i = goalNode; i > 0; i = nodes[i].parent) chain.Add(nodes[i]);
            chain.Reverse();

            var game = new SokobanGame(level);
            var sb = new StringBuilder();
            var dirs = new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right };
            foreach (var n in chain)
            {
                var dir = dirs[n.dir];
                int stand = n.boxFrom - _dirOffset[n.dir];
                var path = game.FindWalkPath(game.ToPos(stand));
                if (path == null) throw new InvalidOperationException("Solver reconstruction failed");
                foreach (var d in path)
                {
                    game.TryMove(d);
                    sb.Append(DirectionUtil.ToChar(d, false));
                }
                if (game.TryMove(dir) != MoveResult.Pushed) throw new InvalidOperationException("Solver push failed");
                sb.Append(DirectionUtil.ToChar(dir, true));
            }
            return new SolveResult
            {
                status = SolveStatus.Solved,
                solution = sb.ToString(),
                moves = sb.Length,
                pushes = chain.Count,
                nodes = expanded
            };
        }

        /// <summary>Tiny binary min-heap of (item, priority), FIFO among equal priorities is not guaranteed.</summary>
        private class MinHeap
        {
            private readonly List<long> _data = new List<long>();
            private long _seq;
            public int Count { get { return _data.Count; } }

            public void Push(int item, int priority)
            {
                // pack priority (high bits) | sequence (mid) | item (low) -> stable ordering
                long key = ((long)priority << 44) | ((_seq++ & 0xFFFFF) << 24) | (uint)item & 0xFFFFFF;
                _data.Add(key);
                int i = _data.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_data[p] <= _data[i]) break;
                    long t = _data[p]; _data[p] = _data[i]; _data[i] = t;
                    i = p;
                }
            }

            public int Pop()
            {
                long top = _data[0];
                int last = _data.Count - 1;
                _data[0] = _data[last];
                _data.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _data.Count && _data[l] < _data[m]) m = l;
                    if (r < _data.Count && _data[r] < _data[m]) m = r;
                    if (m == i) break;
                    long t = _data[m]; _data[m] = _data[i]; _data[i] = t;
                    i = m;
                }
                return (int)(top & 0xFFFFFF);
            }
        }
    }
}
