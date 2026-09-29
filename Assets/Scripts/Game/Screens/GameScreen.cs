using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sokoban.Core;
using Sokoban.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game.Screens
{
    /// <summary>Gameplay: input, HUD, undo/redo, hints, deadlock warnings, win flow.</summary>
    public class GameScreen : ScreenBase
    {
        private const float RepeatDelay = 0.2f;

        private LevelData _level;
        private SokobanGame _game;
        private DeadlockDetector _deadlocks;
        private PlayContext _context;
        private int _index;

        private float _elapsed;
        private bool _started;
        private bool _paused;
        private bool _winShown;
        private float _winDelay = -1f;
        private int _hintsUsed;
        private int _stateVersion;

        // input
        private Direction? _buffered;
        private Direction? _held;
        private float _holdTime;
        private readonly List<Direction> _auto = new List<Direction>();
        private bool _hoverValid;
        private Int2 _hoverCell;

        // hint
        private Task<SolveResult> _hintTask;
        private int _hintVersion;
        private volatile bool _hintCancel;

        // HUD
        private Text _title, _moves, _pushes, _time, _boxes;
        private Button _undo, _redo, _hint;
        private Text _footer;

        public override bool ShowsBoard { get { return true; } }
        public override Vector4 BoardInsets { get { return new Vector4(40, 150, 40, 80); } }
        public PlayContext Context { get { return _context; } }

        protected override void OnBuild()
        {
            var bar = UIFactory.Panel(Root, Theme.PanelBg, "HUD", false);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 120);
            var h = UIFactory.HLayout(bar, 18, new RectOffset(28, 28, 22, 22), TextAnchor.MiddleLeft);
            h.childForceExpandHeight = false;

            UIFactory.Button(bar.transform, "Ⅱ 菜单", Pause, Theme.Button, 28, 150, 72);
            _title = UIFactory.Label(bar.transform, "", 34, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(_title, 240, 72, 1);

            _moves = Stat(bar.transform, "步数");
            _pushes = Stat(bar.transform, "推动");
            _boxes = Stat(bar.transform, "箱子");
            _time = Stat(bar.transform, "时间");

            _undo = UIFactory.Button(bar.transform, "撤销 Z", Undo, Theme.Button, 28, 140, 72);
            _redo = UIFactory.Button(bar.transform, "重做 Y", Redo, Theme.Button, 28, 140, 72);
            UIFactory.Button(bar.transform, "重开 R", ConfirmRestart, Theme.Button, 28, 140, 72);
            _hint = UIFactory.Button(bar.transform, "提示 H", RequestHint, Theme.Hex("#6B5BD6"), 28, 140, 72);

            _footer = UIFactory.Label(Root, "", 24, Theme.TextDim);
            UIFactory.Place(_footer.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1800, 40));
        }

        private Text Stat(Transform parent, string label)
        {
            var box = UIFactory.Panel(parent, new Color(0, 0, 0, 0.22f), "Stat:" + label);
            UIFactory.Layout(box, 128, 76);
            var v = UIFactory.VLayout(box, 0, new RectOffset(6, 6, 4, 4), TextAnchor.MiddleCenter);
            v.childForceExpandHeight = true;
            var l = UIFactory.Label(box.transform, label, 20, Theme.TextDim);
            var value = UIFactory.Label(box.transform, "0", 32, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Layout(l, -1, 26);
            UIFactory.Layout(value, -1, 40);
            return value;
        }

        // ------------------------------------------------------------------ level lifecycle

        public void StartLevel(LevelData level, PlayContext context, int index)
        {
            Modal.CloseAll();
            CancelHint();
            _level = level;
            _context = context;
            _index = index;
            if (_game != null) _game.Changed -= OnGameChanged;
            _game = new SokobanGame(level.Clone());
            _game.Changed += OnGameChanged;
            _deadlocks = new DeadlockDetector(_game);
            App.Board.BoxLandedOnGoal = c => App.Sfx.Play(SfxId.BoxOnGoal, 0.8f);
            App.Board.BuildForPlay(_game);
            _elapsed = 0;
            _started = false;
            _paused = false;
            _winShown = false;
            _winDelay = -1f;
            _hintsUsed = 0;
            _auto.Clear();
            _buffered = null;
            _held = null;
            CameraDirty = true;

            switch (context)
            {
                case PlayContext.BuiltIn: _title.text = "第 " + (index + 1) + " 关 · " + level.title; break;
                case PlayContext.Custom: _title.text = "自定义 · " + level.title; break;
                default: _title.text = "试玩 · " + (string.IsNullOrEmpty(level.title) ? "未命名" : level.title); break;
            }
            var p = App.Save.Get(level.id);
            string best = context != PlayContext.Playtest && p != null && p.completed ? "   最佳 " + p.bestMoves + " 步 " + Stars(p.stars) : "";
            _footer.text = (level.par > 0 ? "参考步数 " + level.par : "") + best +
                           "      方向键/WASD 移动 · 鼠标点击寻路 · Z 撤销 · Y 重做 · R 重开 · H 提示 · Esc/P 菜单";
            UpdateHud();
        }

        private void OnGameChanged()
        {
            _stateVersion++;
            UpdateHud();
        }

        protected override void OnHide()
        {
            CancelHint();
            _auto.Clear();
            App.Board.SetHover(false, default(Int2), Color.white);
        }

        private void UpdateHud()
        {
            if (_game == null) return;
            _moves.text = _game.Moves.ToString();
            _pushes.text = _game.Pushes.ToString();
            _boxes.text = _game.BoxesOnGoal + "/" + _game.BoxCount;
            _undo.interactable = _game.CanUndo;
            _redo.interactable = _game.CanRedo;
        }

        // ------------------------------------------------------------------ actions

        private void DoMove(Direction d)
        {
            if (_game == null || _winShown || _game.IsSolved) return;
            var r = _game.TryMove(d);
            if (r == MoveResult.Blocked)
            {
                App.Board.Sync(false); // update facing
                App.Sfx.Play(SfxId.Blocked, 0.6f);
                _auto.Clear();
                return;
            }
            _started = true;
            App.Board.Sync(true);
            if (r == MoveResult.Pushed)
            {
                App.Sfx.Play(SfxId.Push, 0.9f, Random.Range(0.95f, 1.05f));
                var boxPos = _game.PlayerPos + DirectionUtil.Delta(d);
                if (App.Save.Data.showDeadlockWarnings && _deadlocks.IsBoxDeadlocked(boxPos))
                {
                    App.ShowToast("这个箱子已经推不到目标点了，按 Z 撤销", 3f);
                    App.Sfx.Play(SfxId.Error, 0.5f);
                    _auto.Clear();
                }
            }
            else
            {
                App.Sfx.Play(SfxId.Step, 0.5f, Random.Range(0.9f, 1.1f));
            }
            if (_game.IsSolved)
            {
                _auto.Clear();
                _winDelay = 0.3f;
            }
        }

        public void Undo()
        {
            if (_game == null || _winShown) return;
            _auto.Clear();
            if (_game.Undo())
            {
                App.Board.Sync(true);
                App.Sfx.Play(SfxId.Undo, 0.6f);
                _winDelay = -1f;
            }
        }

        public void Redo()
        {
            if (_game == null || _winShown) return;
            _auto.Clear();
            if (_game.Redo())
            {
                App.Board.Sync(true);
                App.Sfx.Play(SfxId.Step, 0.5f);
                if (_game.IsSolved) _winDelay = 0.3f;
            }
        }

        private void ConfirmRestart()
        {
            if (_game == null) return;
            if (_game.Moves == 0) return;
            Restart();
        }

        public void Restart()
        {
            if (_game == null) return;
            _auto.Clear();
            CancelHint();
            _game.Restart();
            App.Board.Sync(false);
            _elapsed = 0;
            _started = false;
            _winShown = false;
            _winDelay = -1f;
            App.Sfx.Play(SfxId.Undo, 0.6f);
        }

        private void Pause()
        {
            if (_winShown || Modal.AnyOpen) return;
            _paused = true;
            var m = Modal.Create("暂停", 620, 640);
            UIFactory.VLayout(m.Body, 18, null, TextAnchor.MiddleCenter);
            m.OnClosed = () => _paused = false;
            System.Action<string, System.Action, Color> add = (label, act, col) =>
            {
                UIFactory.Button(m.Body, label, () => { m.Close(); act(); }, col, 32, -1, 78);
            };
            add("继续游戏", () => { }, Theme.Accent);
            add("重新开始", Restart, Theme.Button);
            if (_context == PlayContext.Playtest)
            {
                add("返回编辑器", BackToEditor, Theme.Button);
            }
            else
            {
                add("关卡列表", () =>
                {
                    if (_context == PlayContext.Custom) App.Select.ShowCustomTab();
                    else { App.Select.SetTab(false); App.Show(App.Select); }
                }, Theme.Button);
                add("主菜单", () => App.Show(App.Menu), Theme.Button);
            }
            Object.Destroy(m.ButtonRow.gameObject);
        }

        private void BackToEditor()
        {
            App.Show(App.Editor);
        }

        // ------------------------------------------------------------------ hints

        private void RequestHint()
        {
            if (_game == null || _winShown || _game.IsSolved) return;
            if (_hintTask != null) return;
            var snapshot = _game.ToLevelData();
            _hintCancel = false;
            _hintVersion = _stateVersion;
            UIFactory.SetButtonText(_hint, "计算中…");
            _hint.interactable = false;
            _hintTask = Task.Run(() => Solver.SolveAuto(snapshot, 600000, () => _hintCancel));
        }

        private void CancelHint()
        {
            _hintCancel = true;
            _hintTask = null;
            if (_hint != null)
            {
                UIFactory.SetButtonText(_hint, "提示 H");
                _hint.interactable = true;
            }
        }

        private void PollHint()
        {
            if (_hintTask == null || !_hintTask.IsCompleted) return;
            var task = _hintTask;
            _hintTask = null;
            UIFactory.SetButtonText(_hint, "提示 H");
            _hint.interactable = true;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                App.ShowToast("提示计算失败");
                return;
            }
            var res = task.Result;
            if (_hintVersion != _stateVersion) return; // player moved meanwhile; stale
            switch (res.status)
            {
                case SolveStatus.Solved:
                    if (string.IsNullOrEmpty(res.solution)) return;
                    _hintsUsed++;
                    _auto.Clear();
                    // walk to and perform the next push
                    foreach (char c in res.solution)
                    {
                        Direction d;
                        if (!DirectionUtil.TryParse(c, out d)) continue;
                        _auto.Add(d);
                        if (char.IsUpper(c)) break;
                    }
                    App.ShowToast("提示：下一步推动（剩余约 " + res.pushes + " 次推动）");
                    break;
                case SolveStatus.Unsolvable:
                    App.Sfx.Play(SfxId.Error, 0.6f);
                    Modal.Message("当前局面无解", "箱子已经被卡住，无法完成本关。\n可以撤销几步或者重新开始。",
                        new ModalButton("撤销一步", Undo, Theme.Button),
                        new ModalButton("重新开始", Restart, Theme.Accent));
                    break;
                case SolveStatus.LimitReached:
                    App.ShowToast("局面较复杂，暂时无法给出提示");
                    break;
            }
        }

        // ------------------------------------------------------------------ win flow

        private void ShowWin()
        {
            _winShown = true;
            App.Sfx.Play(SfxId.Win);
            int stars = SaveSystem.ComputeStars(_game.Moves, _level.par);
            bool newBest = false;
            LevelProgress prev = null;
            if (_context != PlayContext.Playtest)
            {
                prev = App.Save.Get(_level.id);
                int prevBest = prev != null && prev.completed ? prev.bestMoves : -1;
                newBest = App.Save.RecordWin(_level.id, _game.Moves, _game.Pushes, _elapsed, stars);
                prev = App.Save.Get(_level.id);
                if (prevBest < 0) newBest = false;
            }

            bool lastBuiltIn = _context == PlayContext.BuiltIn && _index >= App.Levels.BuiltIn.Count - 1;
            string title = lastBuiltIn ? "恭喜通关全部关卡！" : "关卡完成！";
            var m = Modal.Create(title, 820, 640);
            m.OnEscape = null;
            UIFactory.VLayout(m.Body, 8, null, TextAnchor.MiddleCenter);
            var starText = UIFactory.Label(m.Body, Stars(stars), 90, Theme.AccentYellow);
            UIFactory.Layout(starText, -1, 110);
            string stats = "步数 <b>" + _game.Moves + "</b>    推动 <b>" + _game.Pushes + "</b>    用时 <b>" + FormatTime(_elapsed) + "</b>";
            UIFactory.Layout(UIFactory.Label(m.Body, stats, 32, Theme.Text), -1, 50);
            string extra = "";
            if (_level.par > 0) extra += "参考步数 " + _level.par;
            if (prev != null) extra += (extra.Length > 0 ? "    " : "") + "最佳 " + prev.bestMoves + " 步";
            if (_hintsUsed > 0) extra += (extra.Length > 0 ? "    " : "") + "使用提示 " + _hintsUsed + " 次";
            UIFactory.Layout(UIFactory.Label(m.Body, extra, 26, Theme.TextDim), -1, 40);
            if (newBest) UIFactory.Layout(UIFactory.Label(m.Body, "新纪录！", 32, Theme.AccentGreen, TextAnchor.MiddleCenter, FontStyle.Bold), -1, 46);
            if (_context == PlayContext.Playtest)
                UIFactory.Layout(UIFactory.Label(m.Body, "试玩通过，关卡可解 √", 28, Theme.AccentGreen), -1, 40);

            m.AddButton(new ModalButton("重玩", Restart, Theme.Button));
            if (_context == PlayContext.Playtest)
            {
                m.AddButton(new ModalButton("返回编辑器", BackToEditor, Theme.Accent));
            }
            else if (_context == PlayContext.BuiltIn)
            {
                m.AddButton(new ModalButton("关卡列表", () => { App.Select.SetTab(false); App.Show(App.Select); }, Theme.Button));
                if (!lastBuiltIn) m.AddButton(new ModalButton("下一关 →", () => App.PlayBuiltIn(_index + 1), Theme.Accent));
                else m.AddButton(new ModalButton("主菜单", () => App.Show(App.Menu), Theme.Accent));
            }
            else
            {
                m.AddButton(new ModalButton("关卡列表", () => App.Select.ShowCustomTab(), Theme.Accent));
            }
        }

        // ------------------------------------------------------------------ per frame

        public override void Tick(bool inputBlocked)
        {
            if (_game == null) return;
            PollHint();

            if (_started && !_paused && !_winShown && !_game.IsSolved && !inputBlocked)
                _elapsed += Time.deltaTime;
            _time.text = FormatTime(_elapsed);

            if (_winDelay >= 0 && !App.Board.IsAnimating)
            {
                _winDelay -= Time.deltaTime;
                if (_winDelay < 0 && _game.IsSolved && !_winShown) ShowWin();
            }

            if (inputBlocked || _winShown)
            {
                App.Board.SetHover(false, default(Int2), Color.white);
                return;
            }
            if (UIFactory.IsTypingInInputField()) return;
            HandleKeyboard();
            HandleMouse();

            if (!App.Board.IsAnimating && !_game.IsSolved)
            {
                if (_buffered.HasValue)
                {
                    var d = _buffered.Value;
                    _buffered = null;
                    DoMove(d);
                }
                else if (_auto.Count > 0)
                {
                    var d = _auto[0];
                    _auto.RemoveAt(0);
                    DoMove(d);
                }
                else if (_held.HasValue && _holdTime >= RepeatDelay)
                {
                    DoMove(_held.Value);
                }
            }
        }

        private void HandleKeyboard()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) { Pause(); return; }
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.U)) { Undo(); return; }
            if (Input.GetKeyDown(KeyCode.Y)) { Redo(); return; }
            if (Input.GetKeyDown(KeyCode.R) && !ctrl) { ConfirmRestart(); return; }
            if (Input.GetKeyDown(KeyCode.H)) { RequestHint(); return; }
            if (Input.GetKeyDown(KeyCode.N) && _context == PlayContext.BuiltIn && App.Save.IsCompleted(_level.id) && _index + 1 < App.Levels.BuiltIn.Count && App.IsBuiltInUnlocked(_index + 1))
            {
                App.PlayBuiltIn(_index + 1);
                return;
            }

            // Held undo repeats too (Z held)
            Direction? pressed = null;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) pressed = Direction.Up;
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) pressed = Direction.Down;
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) pressed = Direction.Left;
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) pressed = Direction.Right;

            if (pressed.HasValue)
            {
                _auto.Clear();
                _buffered = pressed;
                _held = pressed;
                _holdTime = 0;
            }
            else if (_held.HasValue)
            {
                if (IsDirectionHeld(_held.Value)) _holdTime += Time.deltaTime;
                else
                {
                    _held = null;
                    // another direction might still be held
                    foreach (var d in DirectionUtil.All)
                        if (IsDirectionHeld(d)) { _held = d; _holdTime = RepeatDelay; break; }
                }
            }
        }

        private static bool IsDirectionHeld(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);
                case Direction.Down: return Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
                case Direction.Left: return Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
                default: return Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
            }
        }

        private void HandleMouse()
        {
            if (UIFactory.PointerOverUI())
            {
                App.Board.SetHover(false, default(Int2), Color.white);
                return;
            }
            Int2 cell;
            bool inside = App.Board.WorldToCell(App.ScreenToWorld(Input.mousePosition), out cell);
            bool target = inside && !_game.IsWall(cell) && _game.IsInterior(cell) && cell != _game.PlayerPos;
            if (target && (!_hoverValid || cell != _hoverCell))
            {
                _hoverCell = cell;
                _hoverValid = true;
            }
            if (!target) _hoverValid = false;

            Direction pushDir;
            bool pushable = target && _game.IsBox(cell) && AdjacentDirection(_game.PlayerPos, cell, out pushDir) && _game.Probe(pushDir) == MoveResult.Pushed;
            bool walkable = target && !_game.IsBox(cell);
            App.Board.SetHover(target && (pushable || walkable), cell, pushable ? new Color(0.6f, 1f, 0.6f, 0.8f) : new Color(1, 1, 1, 0.45f));

            if (Input.GetMouseButtonDown(0) && target)
            {
                Direction d;
                if (_game.IsBox(cell))
                {
                    if (AdjacentDirection(_game.PlayerPos, cell, out d))
                    {
                        _auto.Clear();
                        _auto.Add(d);
                    }
                    else App.Sfx.Play(SfxId.Blocked, 0.5f);
                    return;
                }
                var path = _game.FindWalkPath(cell);
                if (path == null) { App.Sfx.Play(SfxId.Blocked, 0.5f); return; }
                _auto.Clear();
                _auto.AddRange(path);
            }
            if (Input.GetMouseButtonDown(1)) Undo();
        }

        private static bool AdjacentDirection(Int2 from, Int2 to, out Direction d)
        {
            var diff = to - from;
            d = Direction.Up;
            if (Mathf.Abs(diff.x) + Mathf.Abs(diff.y) != 1) return false;
            if (diff.x == 1) d = Direction.Right;
            else if (diff.x == -1) d = Direction.Left;
            else if (diff.y == 1) d = Direction.Down;
            else d = Direction.Up;
            return true;
        }
    }
}
