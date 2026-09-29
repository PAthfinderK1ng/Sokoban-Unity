using System.Collections.Generic;

namespace Sokoban.Core
{
    /// <summary>
    /// Cheap, instant deadlock checks used during play to warn the player
    /// (the full check is the solver, which runs asynchronously for hints).
    /// </summary>
    public class DeadlockDetector
    {
        private readonly SokobanGame _game;
        private readonly bool[] _live;

        public DeadlockDetector(SokobanGame game)
        {
            _game = game;
            int w = game.Width, h = game.Height;
            _live = new bool[w * h];
            var q = new Queue<Int2>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Int2(x, y);
                    if (game.IsGoal(p) && !game.IsWall(p))
                    {
                        _live[y * w + x] = true;
                        q.Enqueue(p);
                    }
                }
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in DirectionUtil.All)
                {
                    var delta = DirectionUtil.Delta(d);
                    var c1 = c + delta;
                    var c2 = c1 + delta;
                    if (game.IsWall(c1) || game.IsWall(c2)) continue;
                    int i = c1.y * w + c1.x;
                    if (_live[i]) continue;
                    _live[i] = true;
                    q.Enqueue(c1);
                }
            }
        }

        public bool IsDeadSquare(Int2 p)
        {
            return _game.InBounds(p) && !_live[p.y * _game.Width + p.x];
        }

        /// <summary>True if the box at p can provably never reach a goal anymore.</summary>
        public bool IsBoxDeadlocked(Int2 p)
        {
            if (!_game.IsBox(p)) return false;
            if (IsDeadSquare(p)) return true;
            // 2x2 freeze blocks
            for (int ox = -1; ox <= 0; ox++)
                for (int oy = -1; oy <= 0; oy++)
                {
                    bool allBlocked = true, offGoal = false;
                    for (int dx = 0; dx <= 1 && allBlocked; dx++)
                        for (int dy = 0; dy <= 1; dy++)
                        {
                            var c = new Int2(p.x + ox + dx, p.y + oy + dy);
                            bool wall = _game.IsWall(c), box = _game.IsBox(c);
                            if (!wall && !box) { allBlocked = false; break; }
                            if (box && !_game.IsGoal(c)) offGoal = true;
                        }
                    if (allBlocked && offGoal) return true;
                }
            return false;
        }

        public bool AnyBoxDeadlocked()
        {
            foreach (var b in _game.GetBoxes()) if (IsBoxDeadlocked(b)) return true;
            return false;
        }
    }
}
