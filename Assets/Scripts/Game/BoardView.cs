using System.Collections.Generic;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Game
{
    /// <summary>
    /// Renders a level with SpriteRenderers. Two modes:
    ///  - Play: static floor/walls/goals + animated box and player objects driven by a <see cref="SokobanGame"/>.
    ///  - Editor: every cell shows its full content, refreshed from a <see cref="LevelData"/>.
    /// One grid cell == one world unit. Cell (0,0) is the top-left corner.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        private class Mover
        {
            public Transform tr;
            public SpriteRenderer sr;
            public Int2 cell;
            public Vector3 from, to;
            public float t = 1f;
            public float punch;
        }

        [System.NonSerialized] public SpriteFactory Art;
        public float MoveDuration = 0.11f;

        public int Width { get; private set; }
        public int Height { get; private set; }

        private Transform _staticRoot;
        private SpriteRenderer[] _base;
        private SpriteRenderer[] _goal;
        private SpriteRenderer[] _obj;      // editor only
        private SpriteRenderer _hover;
        private readonly List<Mover> _boxes = new List<Mover>();
        private Mover _player;
        private SokobanGame _game;
        private bool _editorMode;

        public bool IsAnimating
        {
            get
            {
                if (_player != null && _player.t < 1f) return true;
                foreach (var b in _boxes) if (b.t < 1f) return true;
                return false;
            }
        }

        /// <summary>Raised when a box newly lands on a goal during a synced move.</summary>
        public System.Action<Int2> BoxLandedOnGoal;

        // ------------------------------------------------------------------ coordinates

        public Vector3 CellToWorld(Int2 c)
        {
            return new Vector3(c.x - (Width - 1) * 0.5f, (Height - 1) * 0.5f - c.y, 0f);
        }

        public bool WorldToCell(Vector3 w, out Int2 cell)
        {
            int x = Mathf.RoundToInt(w.x + (Width - 1) * 0.5f);
            int y = Mathf.RoundToInt((Height - 1) * 0.5f - w.y);
            cell = new Int2(x, y);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public Vector2 BoardSize { get { return new Vector2(Width, Height); } }

        // ------------------------------------------------------------------ building

        public void Clear()
        {
            if (_staticRoot != null) Destroy(_staticRoot.gameObject);
            _staticRoot = null;
            foreach (var b in _boxes) if (b.tr != null) Destroy(b.tr.gameObject);
            _boxes.Clear();
            if (_player != null && _player.tr != null) Destroy(_player.tr.gameObject);
            _player = null;
            _game = null;
            _base = _goal = _obj = null;
            _hover = null;
            Width = Height = 0;
        }

        private SpriteRenderer NewRenderer(string name, Transform parent, Vector3 pos, Sprite s, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.sortingOrder = order;
            return sr;
        }

        private void BuildGrid(int w, int h)
        {
            Width = w;
            Height = h;
            _staticRoot = new GameObject("Static").transform;
            _staticRoot.SetParent(transform, false);
            _base = new SpriteRenderer[w * h];
            _goal = new SpriteRenderer[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = CellToWorld(new Int2(x, y));
                    int i = y * w + x;
                    _base[i] = NewRenderer("c" + x + "_" + y, _staticRoot, p, null, 0);
                    _goal[i] = NewRenderer("g", _base[i].transform, Vector3.zero, Art.Goal, 1);
                    _goal[i].enabled = false;
                }
            _hover = NewRenderer("Hover", _staticRoot, Vector3.zero, Art.Highlight, 20);
            _hover.color = new Color(1, 1, 1, 0.55f);
            _hover.enabled = false;
        }

        public void BuildForPlay(SokobanGame game)
        {
            Clear();
            _editorMode = false;
            _game = game;
            BuildGrid(game.Width, game.Height);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    var c = new Int2(x, y);
                    int i = y * Width + x;
                    if (game.IsWall(c)) _base[i].sprite = Art.Wall;
                    else if (game.IsInterior(c)) _base[i].sprite = ((x + y) & 1) == 0 ? Art.FloorA : Art.FloorB;
                    else _base[i].sprite = null;
                    _goal[i].enabled = game.IsGoal(c);
                }

            foreach (var b in game.GetBoxes())
            {
                var sr = NewRenderer("Box", transform, CellToWorld(b), Art.Box, 5);
                var m = new Mover { tr = sr.transform, sr = sr, cell = b, from = sr.transform.localPosition, to = sr.transform.localPosition };
                var sh = NewRenderer("Shadow", sr.transform, new Vector3(0.03f, -0.08f, 0), Art.Shadow, 4);
                sh.transform.localScale = new Vector3(1.05f, 0.9f, 1);
                _boxes.Add(m);
            }
            var psr = NewRenderer("Player", transform, CellToWorld(game.PlayerPos), Art.Player[(int)Direction.Down], 7);
            _player = new Mover { tr = psr.transform, sr = psr, cell = game.PlayerPos, from = psr.transform.localPosition, to = psr.transform.localPosition };
            var ps = NewRenderer("Shadow", psr.transform, new Vector3(0, -0.2f, 0), Art.Shadow, 6);
            ps.transform.localScale = new Vector3(0.8f, 0.5f, 1);
            Sync(false);
        }

        public void BuildForEditor(LevelData level)
        {
            Clear();
            _editorMode = true;
            BuildGrid(level.width, level.height);
            _obj = new SpriteRenderer[Width * Height];
            for (int i = 0; i < _obj.Length; i++)
            {
                _obj[i] = NewRenderer("o", _base[i].transform, Vector3.zero, null, 5);
                var grid = NewRenderer("grid", _base[i].transform, Vector3.zero, Art.EditorCell, 2);
                grid.color = Color.white;
            }
            RefreshEditor(level);
        }

        public void RefreshEditor(LevelData level)
        {
            if (!_editorMode || level.width != Width || level.height != Height)
            {
                BuildForEditor(level);
                return;
            }
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    char c = level.Get(x, y);
                    if (c == Tiles.Wall)
                    {
                        _base[i].sprite = Art.Wall;
                        _base[i].color = Color.white;
                    }
                    else
                    {
                        _base[i].sprite = ((x + y) & 1) == 0 ? Art.FloorA : Art.FloorB;
                        _base[i].color = new Color(0.8f, 0.8f, 0.85f, 1f);
                    }
                    _goal[i].enabled = Tiles.HasGoal(c);
                    if (Tiles.HasBox(c)) _obj[i].sprite = Tiles.HasGoal(c) ? Art.BoxOnGoal : Art.Box;
                    else if (Tiles.HasPlayer(c)) _obj[i].sprite = Art.Player[(int)Direction.Down];
                    else _obj[i].sprite = null;
                }
        }

        public void SetHover(bool visible, Int2 cell, Color color)
        {
            if (_hover == null) return;
            _hover.enabled = visible;
            if (!visible) return;
            _hover.transform.localPosition = CellToWorld(cell);
            _hover.color = color;
        }

        // ------------------------------------------------------------------ play mode sync

        /// <summary>Update box/player objects from the game state. animate=false snaps instantly.</summary>
        public void Sync(bool animate)
        {
            if (_game == null) return;
            var state = new HashSet<Int2>(_game.GetBoxes());
            var removed = new List<Mover>();
            var matched = new HashSet<Int2>();
            foreach (var b in _boxes)
            {
                if (state.Contains(b.cell)) matched.Add(b.cell);
                else removed.Add(b);
            }
            var added = new List<Int2>();
            foreach (var c in state) if (!matched.Contains(c)) added.Add(c);

            bool single = removed.Count == 1 && added.Count == 1;
            for (int i = 0; i < removed.Count && i < added.Count; i++)
            {
                var m = removed[i];
                bool wasOnGoal = _game.IsGoal(m.cell);
                m.cell = added[i];
                MoveTo(m, CellToWorld(m.cell), animate && single);
                if (!wasOnGoal && _game.IsGoal(m.cell) && animate)
                {
                    m.punch = 1f;
                    if (BoxLandedOnGoal != null) BoxLandedOnGoal(m.cell);
                }
            }
            foreach (var b in _boxes) b.sr.sprite = _game.IsGoal(b.cell) ? Art.BoxOnGoal : Art.Box;

            var pp = _game.PlayerPos;
            bool adjacent = Mathf.Abs(pp.x - _player.cell.x) + Mathf.Abs(pp.y - _player.cell.y) == 1;
            _player.cell = pp;
            MoveTo(_player, CellToWorld(pp), animate && adjacent);
            _player.sr.sprite = Art.Player[(int)_game.LastDirection];
        }

        private void MoveTo(Mover m, Vector3 target, bool animate)
        {
            if (animate)
            {
                m.from = m.tr.localPosition;
                m.to = target;
                m.t = 0f;
            }
            else
            {
                m.from = m.to = target;
                m.tr.localPosition = target;
                m.t = 1f;
            }
        }

        public void FinishAnimations()
        {
            if (_player != null) { _player.t = 1f; _player.tr.localPosition = _player.to; }
            foreach (var b in _boxes) { b.t = 1f; b.tr.localPosition = b.to; }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float dur = Mathf.Max(0.01f, MoveDuration);
            if (_player != null) Step(_player, dt, dur, true);
            foreach (var b in _boxes) Step(b, dt, dur, false);
        }

        private void Step(Mover m, float dt, float dur, bool isPlayer)
        {
            if (m.t < 1f)
            {
                m.t = Mathf.Min(1f, m.t + dt / dur);
                float e = 1f - (1f - m.t) * (1f - m.t); // ease out
                m.tr.localPosition = Vector3.LerpUnclamped(m.from, m.to, e);
            }
            float scale = 1f;
            if (isPlayer && m.t < 1f)
            {
                float s = Mathf.Sin(m.t * Mathf.PI) * 0.08f;
                m.tr.localScale = new Vector3(1f + s, 1f - s, 1f);
            }
            else if (isPlayer)
            {
                // idle breathing
                float b = Mathf.Sin(Time.time * 3f) * 0.015f;
                m.tr.localScale = new Vector3(1f - b, 1f + b, 1f);
            }
            if (!isPlayer)
            {
                if (m.punch > 0f)
                {
                    m.punch = Mathf.Max(0f, m.punch - dt * 4f);
                    scale = 1f + Mathf.Sin(m.punch * Mathf.PI) * 0.15f;
                }
                m.tr.localScale = new Vector3(scale, scale, 1f);
            }
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
