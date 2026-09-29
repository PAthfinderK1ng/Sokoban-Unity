using System;
using System.Collections.Generic;
using System.Text;

namespace Sokoban.Core
{
    public enum MoveResult
    {
        Blocked = 0,
        Moved = 1,
        Pushed = 2
    }

    /// <summary>
    /// Runtime state of a level being played: player / box positions, move history with undo & redo.
    /// Pure C#, no engine dependency, fully unit-testable.
    /// </summary>
    public class SokobanGame
    {
        private struct Step
        {
            public Direction dir;
            public bool push;
        }

        public readonly int Width;
        public readonly int Height;
        public LevelData Level { get; private set; }

        private readonly bool[] _wall;
        private readonly bool[] _goal;
        private readonly bool[] _box;
        private readonly bool[] _interior;
        private readonly int _startPlayer;
        private readonly bool[] _startBoxes;

        private int _player;
        private int _boxesOnGoal;
        private readonly List<Step> _history = new List<Step>();
        private readonly List<Step> _redo = new List<Step>();

        public int Moves { get { return _history.Count; } }
        public int Pushes { get; private set; }
        public int BoxCount { get; private set; }
        public int GoalCount { get; private set; }
        public int BoxesOnGoal { get { return _boxesOnGoal; } }
        public bool IsSolved { get { return BoxCount > 0 && _boxesOnGoal == BoxCount; } }
        public bool CanUndo { get { return _history.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }
        public Int2 PlayerPos { get { return ToPos(_player); } }
        public Direction LastDirection { get; private set; }

        /// <summary>Raised after every successful move / undo / redo.</summary>
        public event Action Changed;

        public SokobanGame(LevelData level)
        {
            if (level == null) throw new ArgumentNullException("level");
            Level = level;
            Width = level.width;
            Height = level.height;
            int n = Width * Height;
            _wall = new bool[n];
            _goal = new bool[n];
            _box = new bool[n];
            _startBoxes = new bool[n];
            _player = -1;
            LastDirection = Direction.Down;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    char c = level.Get(x, y);
                    int i = y * Width + x;
                    _wall[i] = c == Tiles.Wall;
                    _goal[i] = Tiles.HasGoal(c);
                    _box[i] = Tiles.HasBox(c);
                    if (Tiles.HasPlayer(c)) _player = i;
                    if (_box[i]) BoxCount++;
                    if (_goal[i]) GoalCount++;
                    if (_box[i] && _goal[i]) _boxesOnGoal++;
                }
            }
            if (_player < 0) throw new ArgumentException("Level has no player");
            Array.Copy(_box, _startBoxes, n);
            _startPlayer = _player;
            _interior = ComputeInterior();
        }

        // ------------------------------------------------------------------ queries

        public bool InBounds(Int2 p) { return p.x >= 0 && p.y >= 0 && p.x < Width && p.y < Height; }
        public int ToIndex(Int2 p) { return p.y * Width + p.x; }
        public Int2 ToPos(int i) { return new Int2(i % Width, i / Width); }

        public bool IsWall(Int2 p) { return !InBounds(p) || _wall[ToIndex(p)]; }
        public bool IsGoal(Int2 p) { return InBounds(p) && _goal[ToIndex(p)]; }
        public bool IsBox(Int2 p) { return InBounds(p) && _box[ToIndex(p)]; }

        /// <summary>Floor cells reachable from the start (ignoring boxes). Used to decide which floor tiles to draw.</summary>
        public bool IsInterior(Int2 p) { return InBounds(p) && _interior[ToIndex(p)]; }

        public List<Int2> GetBoxes()
        {
            var list = new List<Int2>(BoxCount);
            for (int i = 0; i < _box.Length; i++) if (_box[i]) list.Add(ToPos(i));
            return list;
        }

        private bool[] ComputeInterior()
        {
            var seen = new bool[Width * Height];
            var q = new Queue<int>();
            q.Enqueue(_player);
            seen[_player] = true;
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                var p = ToPos(c);
                foreach (var d in DirectionUtil.All)
                {
                    var np = p + DirectionUtil.Delta(d);
                    if (!InBounds(np)) continue;
                    int ni = ToIndex(np);
                    if (seen[ni] || _wall[ni]) continue;
                    seen[ni] = true;
                    q.Enqueue(ni);
                }
            }
            // Boxes / goals outside the reachable area are still interior for drawing purposes.
            for (int i = 0; i < seen.Length; i++) if (_box[i] || _goal[i]) seen[i] = true;
            return seen;
        }

        // ------------------------------------------------------------------ actions

        public MoveResult TryMove(Direction d)
        {
            var r = Apply(d);
            if (r != MoveResult.Blocked)
            {
                _history.Add(new Step { dir = d, push = r == MoveResult.Pushed });
                _redo.Clear();
                RaiseChanged();
            }
            else
            {
                LastDirection = d;
            }
            return r;
        }

        /// <summary>What would happen if the player moved in direction d (no state change).</summary>
        public MoveResult Probe(Direction d)
        {
            var delta = DirectionUtil.Delta(d);
            var p = PlayerPos;
            var next = p + delta;
            if (IsWall(next)) return MoveResult.Blocked;
            if (!IsBox(next)) return MoveResult.Moved;
            var beyond = next + delta;
            if (IsWall(beyond) || IsBox(beyond)) return MoveResult.Blocked;
            return MoveResult.Pushed;
        }

        private MoveResult Apply(Direction d)
        {
            var r = Probe(d);
            if (r == MoveResult.Blocked) return r;
            var delta = DirectionUtil.Delta(d);
            var next = PlayerPos + delta;
            if (r == MoveResult.Pushed)
            {
                MoveBox(ToIndex(next), ToIndex(next + delta));
                Pushes++;
            }
            _player = ToIndex(next);
            LastDirection = d;
            return r;
        }

        private void MoveBox(int from, int to)
        {
            _box[from] = false;
            if (_goal[from]) _boxesOnGoal--;
            _box[to] = true;
            if (_goal[to]) _boxesOnGoal++;
        }

        public bool Undo()
        {
            if (_history.Count == 0) return false;
            var s = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            var delta = DirectionUtil.Delta(s.dir);
            var p = PlayerPos;
            if (s.push)
            {
                MoveBox(ToIndex(p + delta), ToIndex(p));
                Pushes--;
            }
            _player = ToIndex(p - delta);
            LastDirection = s.dir;
            _redo.Add(s);
            RaiseChanged();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            var s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            var r = Apply(s.dir);
            if (r == MoveResult.Blocked) { _redo.Clear(); return false; }
            _history.Add(s);
            RaiseChanged();
            return true;
        }

        public void Restart()
        {
            Array.Copy(_startBoxes, _box, _box.Length);
            _player = _startPlayer;
            _boxesOnGoal = 0;
            for (int i = 0; i < _box.Length; i++) if (_box[i] && _goal[i]) _boxesOnGoal++;
            _history.Clear();
            _redo.Clear();
            Pushes = 0;
            LastDirection = Direction.Down;
            RaiseChanged();
        }

        /// <summary>Plays a LURD string; stops at the first blocked step. Returns number of steps applied.</summary>
        public int PlayMoves(string lurd)
        {
            int applied = 0;
            if (string.IsNullOrEmpty(lurd)) return 0;
            foreach (char c in lurd)
            {
                Direction d;
                if (!DirectionUtil.TryParse(c, out d)) continue;
                if (TryMove(d) == MoveResult.Blocked) break;
                applied++;
            }
            return applied;
        }

        public string GetHistoryLurd()
        {
            var sb = new StringBuilder(_history.Count);
            foreach (var s in _history) sb.Append(DirectionUtil.ToChar(s.dir, s.push));
            return sb.ToString();
        }

        /// <summary>Snapshot of the current position as a level (for solver / hints).</summary>
        public LevelData ToLevelData()
        {
            var l = Level.Clone();
            for (int y = 0; y < Height; y++)
            {
                var sb = new StringBuilder(Width);
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    sb.Append(Tiles.Compose(_wall[i], _goal[i], _box[i], i == _player));
                }
                l.rows[y] = sb.ToString();
            }
            return l;
        }

        // ------------------------------------------------------------------ path finding (click-to-move)

        /// <summary>Shortest walking path (no pushes) from the player to target, or null if unreachable.</summary>
        public List<Direction> FindWalkPath(Int2 target)
        {
            if (!InBounds(target) || IsWall(target) || IsBox(target)) return null;
            int start = _player, goal = ToIndex(target);
            if (start == goal) return new List<Direction>();
            var prev = new int[Width * Height];
            var prevDir = new Direction[Width * Height];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            prev[start] = -1;
            var q = new Queue<int>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                if (c == goal) break;
                var p = ToPos(c);
                foreach (var d in DirectionUtil.All)
                {
                    var np = p + DirectionUtil.Delta(d);
                    if (IsWall(np) || IsBox(np)) continue;
                    int ni = ToIndex(np);
                    if (prev[ni] != -2) continue;
                    prev[ni] = c;
                    prevDir[ni] = d;
                    q.Enqueue(ni);
                }
            }
            if (prev[goal] == -2) return null;
            var path = new List<Direction>();
            for (int c = goal; c != start; c = prev[c]) path.Add(prevDir[c]);
            path.Reverse();
            return path;
        }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}
