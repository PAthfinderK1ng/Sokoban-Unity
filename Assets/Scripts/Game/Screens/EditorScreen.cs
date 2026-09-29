using System.Collections.Generic;
using System.Threading.Tasks;
using Sokoban.Core;
using Sokoban.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game.Screens
{
    public enum EditorTool
    {
        Wall = 0,
        Floor = 1,
        Goal = 2,
        Box = 3,
        Player = 4
    }

    /// <summary>
    /// Level editor: paint tiles with the mouse, resize/shift/trim, undo/redo, validation,
    /// asynchronous solvability check, play testing, save/load, text import/export and random generation.
    /// </summary>
    public class EditorScreen : ScreenBase
    {
        private const int DefaultW = 10, DefaultH = 8;
        private const int UndoLimit = 200;

        private LevelData _work;
        private string _editingId;
        private bool _dirty;
        private int _version;
        private EditorTool _tool = EditorTool.Wall;
        private readonly List<LevelData> _undo = new List<LevelData>();
        private readonly List<LevelData> _redo = new List<LevelData>();

        // painting
        private bool _stroking;
        private bool _strokeErase;
        private bool _strokeChanged;
        private Int2 _lastPainted;

        // solver
        private Task<SolveResult> _solveTask;
        private volatile bool _solveCancel;
        private int _solveVersion;
        private SolveResult _solveResult;
        private int _solveResultVersion = -1;
        private bool _saveAfterSolve;
        private Modal _solveModal;

        // generator
        private Task<LevelData> _genTask;
        private volatile bool _genCancel;

        // UI
        private InputField _name;
        private Text _fileLabel, _sizeLabel, _stats, _issues, _solveLabel, _footer;
        private readonly Button[] _toolButtons = new Button[5];
        private Button _undoBtn, _redoBtn, _checkBtn;

        public override bool ShowsBoard { get { return true; } }
        public override Vector4 BoardInsets { get { return new Vector4(470, 30, 440, 70); } }

        private static readonly string[] ToolNames = { "墙壁", "地板", "目标点", "箱子", "玩家" };

        // ------------------------------------------------------------------ UI construction

        protected override void OnBuild()
        {
            BuildLeftPanel();
            BuildRightPanel();
            _footer = UIFactory.Label(Root, "左键绘制 · 右键擦除 · 1-5 切换工具 · Ctrl+Z/Y 撤销重做 · Ctrl+S 保存 · F5 试玩", 24, Theme.TextDim);
            UIFactory.Place(_footer.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(15, 22), new Vector2(1000, 40));
        }

        private RectTransform SidePanel(string name, bool left, float width)
        {
            var p = UIFactory.Panel(Root, Theme.PanelBg, name, false);
            var rt = p.rectTransform;
            rt.anchorMin = new Vector2(left ? 0 : 1, 0);
            rt.anchorMax = new Vector2(left ? 0 : 1, 1);
            rt.pivot = new Vector2(left ? 0 : 1, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);
            rt.anchoredPosition = Vector2.zero;
            var v = UIFactory.VLayout(p, 10, new RectOffset(26, 26, 22, 22));
            v.childForceExpandHeight = false;
            return rt;
        }

        private Transform Row(Transform parent, float height = 60, float spacing = 10)
        {
            var row = UIFactory.Rect("Row", parent);
            var h = UIFactory.HLayout(row, spacing, null, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true;
            UIFactory.Layout(row, -1, height);
            return row;
        }

        private Text Section(Transform parent, string title)
        {
            var t = UIFactory.Label(parent, title, 24, Theme.TextDim, TextAnchor.LowerLeft);
            UIFactory.Layout(t, -1, 30);
            return t;
        }

        private void BuildLeftPanel()
        {
            var p = SidePanel("Left", true, 450);

            var head = Row(p, 64);
            UIFactory.Button(head, "← 返回", ConfirmExit, Theme.Button, 26, 150, 60);
            var title = UIFactory.Label(head, "关卡编辑器", 36, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Layout(title, -1, 60, 1);

            _fileLabel = UIFactory.Label(p, "", 22, Theme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Layout(_fileLabel, -1, 30);
            _name = UIFactory.Input(p, "输入关卡名称…", false, 28);
            UIFactory.Layout(_name, -1, 64);
            _name.characterLimit = 24;
            _name.onValueChanged.AddListener(v =>
            {
                if (_work == null || _work.title == v) return;
                _work.title = v;
                _dirty = true;
                RefreshInfo();
            });

            Section(p, "绘制工具（右键 = 擦除）");
            var grid = UIFactory.Rect("Tools", p);
            UIFactory.Layout(grid, -1, 196);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(124, 92);
            g.spacing = new Vector2(10, 10);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 3;
            g.childAlignment = TextAnchor.UpperCenter;
            Sprite[] icons = { App.Art.Wall, App.Art.FloorA, App.Art.Goal, App.Art.Box, App.Art.Player[1] };
            for (int i = 0; i < 5; i++)
            {
                int ti = i;
                var b = UIFactory.Button(grid, "", () => SetTool((EditorTool)ti), Theme.Button, 22, -1, -1);
                var lbl = b.GetComponentInChildren<Text>();
                lbl.text = ToolNames[i] + " <size=18><color=#9AA3BD>" + (i + 1) + "</color></size>";
                lbl.alignment = TextAnchor.LowerCenter;
                UIFactory.Stretch(lbl.rectTransform, 4, 4, 4, 6);
                var icon = UIFactory.Icon(b.transform, icons[i], 46);
                UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(46, 46));
                _toolButtons[i] = b;
            }

            _sizeLabel = Section(p, "尺寸");
            var sizeRow = Row(p, 56);
            UIFactory.Button(sizeRow, "宽－", () => ResizeBy(-1, 0), Theme.Button, 24);
            UIFactory.Button(sizeRow, "宽＋", () => ResizeBy(1, 0), Theme.Button, 24);
            UIFactory.Button(sizeRow, "高－", () => ResizeBy(0, -1), Theme.Button, 24);
            UIFactory.Button(sizeRow, "高＋", () => ResizeBy(0, 1), Theme.Button, 24);

            Section(p, "整体平移 / 整理");
            var shiftRow = Row(p, 56);
            UIFactory.Button(shiftRow, "←", () => ShiftBy(-1, 0), Theme.Button, 28);
            UIFactory.Button(shiftRow, "↑", () => ShiftBy(0, -1), Theme.Button, 28);
            UIFactory.Button(shiftRow, "↓", () => ShiftBy(0, 1), Theme.Button, 28);
            UIFactory.Button(shiftRow, "→", () => ShiftBy(1, 0), Theme.Button, 28);
            var tidy = Row(p, 56);
            UIFactory.Button(tidy, "自动围墙", AutoWalls, Theme.Button, 24);
            UIFactory.Button(tidy, "裁剪空白", TrimLevel, Theme.Button, 24);

            Section(p, "编辑");
            var edit = Row(p, 56);
            _undoBtn = UIFactory.Button(edit, "撤销", Undo, Theme.Button, 24);
            _redoBtn = UIFactory.Button(edit, "重做", Redo, Theme.Button, 24);
            UIFactory.Button(edit, "清空", () => Modal.Confirm("清空关卡", "清空当前画布上的所有内容？（可撤销）", "清空", ClearLevel, true), Theme.Button, 24);
            var gen = Row(p, 56);
            UIFactory.Button(gen, "随机生成关卡", AskGenerate, Theme.Hex("#6B5BD6"), 24);
        }

        private void BuildRightPanel()
        {
            var p = SidePanel("Right", false, 420);

            Section(p, "关卡文件");
            var fileRow = Row(p, 56);
            UIFactory.Button(fileRow, "新建", ConfirmNew, Theme.Button, 24);
            UIFactory.Button(fileRow, "打开", OpenDialog, Theme.Button, 24);
            var io = Row(p, 56);
            UIFactory.Button(io, "导入文本", ImportDialog, Theme.Button, 24);
            UIFactory.Button(io, "导出文本", Export, Theme.Button, 24);

            Section(p, "统计");
            _stats = UIFactory.Label(p, "", 26, Theme.Text, TextAnchor.UpperLeft);
            UIFactory.Layout(_stats, -1, 76);

            Section(p, "检查结果");
            _issues = UIFactory.Label(p, "", 24, Theme.Text, TextAnchor.UpperLeft);
            UIFactory.Layout(_issues, -1, 150);

            Section(p, "可解性");
            _solveLabel = UIFactory.Label(p, "", 24, Theme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Layout(_solveLabel, -1, 70);
            _checkBtn = UIFactory.Button(p, "检查可解性", () => StartSolve(false), Theme.Button, 26, -1, 64);

            UIFactory.Spacer(p, -1, 10, true);
            UIFactory.Button(p, "▶ 试玩", Playtest, Theme.AccentGreen, 30, -1, 76);
            var saveRow = Row(p, 76);
            UIFactory.Button(saveRow, "保存", () => SaveFlow(false), Theme.Accent, 28);
            UIFactory.Button(saveRow, "另存为", () => SaveFlow(true), Theme.Button, 28);
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShow()
        {
            if (_work == null) NewLevel();
            else
            {
                App.Board.BuildForEditor(_work);
                CameraDirty = true;
                RefreshAll();
            }
        }

        protected override void OnHide()
        {
            _stroking = false;
            App.Board.SetHover(false, default(Int2), Color.white);
        }

        public void NewLevelIfEmpty()
        {
            if (_work == null) NewLevel();
        }

        public void NewLevel()
        {
            var l = new LevelData(DefaultW, DefaultH);
            for (int x = 0; x < DefaultW; x++) { l.Set(x, 0, Tiles.Wall); l.Set(x, DefaultH - 1, Tiles.Wall); }
            for (int y = 0; y < DefaultH; y++) { l.Set(0, y, Tiles.Wall); l.Set(DefaultW - 1, y, Tiles.Wall); }
            l.title = "";
            Load(l, null);
        }

        /// <summary>Open an existing level (custom: edit in place; built-in: edit a copy).</summary>
        public void OpenForEdit(LevelData level)
        {
            App.Show(this);
            bool custom = !App.Levels.IsBuiltIn(level);
            System.Action open = () =>
            {
                var copy = level.Clone();
                if (!custom)
                {
                    copy.id = "";
                    copy.title = level.title + " (副本)";
                }
                Load(copy, custom ? level.id : null);
            };
            if (_dirty) Modal.Confirm("放弃修改？", "当前关卡有未保存的修改，确定要打开其他关卡吗？", "放弃并打开", open, true);
            else open();
        }

        private void Load(LevelData level, string id)
        {
            CancelSolve();
            _work = level;
            _editingId = id;
            _dirty = false;
            _undo.Clear();
            _redo.Clear();
            _version++;
            _solveResult = null;
            _name.SetTextWithoutNotify(level.title ?? "");
            App.Board.BuildForEditor(_work);
            CameraDirty = true;
            RefreshAll();
        }

        // ------------------------------------------------------------------ editing ops

        private void PushUndo()
        {
            _undo.Add(_work.Clone());
            if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void Changed(bool sizeChanged)
        {
            _dirty = true;
            _version++;
            if (sizeChanged)
            {
                App.Board.BuildForEditor(_work);
                CameraDirty = true;
            }
            else App.Board.RefreshEditor(_work);
            RefreshAll();
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(_work.Clone());
            RestoreSnapshot(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(_work.Clone());
            RestoreSnapshot(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
        }

        private void RestoreSnapshot(LevelData snap)
        {
            bool size = snap.width != _work.width || snap.height != _work.height;
            string title = _work.title;
            _work = snap.Clone();
            _work.title = title;
            _work.id = _editingId ?? "";
            Changed(size);
        }

        private void SetTool(EditorTool t)
        {
            _tool = t;
            RefreshTools();
        }

        private void ResizeBy(int dw, int dh)
        {
            int w = Mathf.Clamp(_work.width + dw, LevelValidator.MinSize, LevelValidator.MaxSize);
            int h = Mathf.Clamp(_work.height + dh, LevelValidator.MinSize, LevelValidator.MaxSize);
            if (w == _work.width && h == _work.height) return;
            PushUndo();
            _work.Resize(w, h);
            Changed(true);
        }

        private void ShiftBy(int dx, int dy)
        {
            PushUndo();
            _work.Shift(dx, dy);
            Changed(false);
        }

        private void AutoWalls()
        {
            PushUndo();
            // Surround every non-empty cell region with walls: wall the border of the canvas,
            // then remove walls that do not touch any floor/object (keeps shapes tidy).
            bool expanded = false;
            for (int x = 0; x < _work.width && !expanded; x++)
                if (IsContent(x, 0) || IsContent(x, _work.height - 1)) expanded = true;
            for (int y = 0; y < _work.height && !expanded; y++)
                if (IsContent(0, y) || IsContent(_work.width - 1, y)) expanded = true;
            if (expanded && _work.width + 2 <= LevelValidator.MaxSize && _work.height + 2 <= LevelValidator.MaxSize)
            {
                _work.Resize(_work.width + 2, _work.height + 2);
                _work.Shift(1, 1);
            }
            for (int x = 0; x < _work.width; x++) { _work.Set(x, 0, Tiles.Wall); _work.Set(x, _work.height - 1, Tiles.Wall); }
            for (int y = 0; y < _work.height; y++) { _work.Set(0, y, Tiles.Wall); _work.Set(_work.width - 1, y, Tiles.Wall); }
            Changed(expanded);
        }

        private bool IsContent(int x, int y)
        {
            char c = _work.Get(x, y);
            return c != Tiles.Wall && c != Tiles.Floor;
        }

        private void TrimLevel()
        {
            PushUndo();
            int w = _work.width, h = _work.height;
            _work.Trim();
            if (_work.width < LevelValidator.MinSize || _work.height < LevelValidator.MinSize)
                _work.Resize(Mathf.Max(_work.width, LevelValidator.MinSize), Mathf.Max(_work.height, LevelValidator.MinSize));
            Changed(w != _work.width || h != _work.height);
        }

        private void ClearLevel()
        {
            PushUndo();
            for (int y = 0; y < _work.height; y++) _work.rows[y] = new string(Tiles.Floor, _work.width);
            Changed(false);
        }

        private bool Paint(Int2 c, EditorTool tool, bool erase)
        {
            char old = _work.Get(c.x, c.y);
            char n;
            if (erase) n = Tiles.Floor;
            else
            {
                switch (tool)
                {
                    case EditorTool.Wall: n = Tiles.Wall; break;
                    case EditorTool.Floor: n = Tiles.Floor; break;
                    case EditorTool.Goal:
                        n = Tiles.HasBox(old) ? Tiles.BoxOnGoal : Tiles.HasPlayer(old) ? Tiles.PlayerOnGoal : Tiles.Goal;
                        break;
                    case EditorTool.Box:
                        n = Tiles.HasGoal(old) ? Tiles.BoxOnGoal : Tiles.Box;
                        break;
                    default:
                        // only one player: remove existing ones
                        for (int y = 0; y < _work.height; y++)
                            for (int x = 0; x < _work.width; x++)
                            {
                                char o = _work.Get(x, y);
                                if (o == Tiles.Player) _work.Set(x, y, Tiles.Floor);
                                else if (o == Tiles.PlayerOnGoal) _work.Set(x, y, Tiles.Goal);
                            }
                        old = _work.Get(c.x, c.y);
                        n = Tiles.HasGoal(old) ? Tiles.PlayerOnGoal : Tiles.Player;
                        break;
                }
            }
            if (n == old && tool != EditorTool.Player) return false;
            _work.Set(c.x, c.y, n);
            return true;
        }

        // ------------------------------------------------------------------ info panels

        private void RefreshAll()
        {
            RefreshTools();
            RefreshInfo();
        }

        private void RefreshTools()
        {
            for (int i = 0; i < _toolButtons.Length; i++)
                UIFactory.SetButtonColor(_toolButtons[i], (int)_tool == i ? Theme.ButtonSelected : Theme.Button);
        }

        private void RefreshInfo()
        {
            if (_work == null) return;
            string file = _editingId == null ? "新关卡（未保存）" : "编辑：" + _editingId;
            _fileLabel.text = file + (_dirty ? "  <color=#F2C14E>● 未保存</color>" : "");
            _sizeLabel.text = "尺寸  " + _work.width + " × " + _work.height + "（" + LevelValidator.MinSize + "~" + LevelValidator.MaxSize + "）";
            int boxes = _work.CountBoxes(), goals = _work.CountGoals(), players = _work.CountPlayers();
            _stats.text = "箱子 " + boxes + "    目标 " + goals + "    玩家 " + players + "\n尺寸 " + _work.width + " × " + _work.height;

            var issues = LevelValidator.Validate(_work);
            if (issues.Count == 0) _issues.text = "<color=#4CB86A>√ 结构检查通过</color>";
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var s in issues) sb.Append("<color=#E0605E>• </color>").Append(s).Append('\n');
                _issues.text = sb.ToString();
            }

            if (_solveTask != null) _solveLabel.text = "正在求解…";
            else if (_solveResult == null || _solveResultVersion != _version) _solveLabel.text = issues.Count == 0 ? "未检查（保存时会自动检查）" : "修复上方问题后可检查";
            else _solveLabel.text = DescribeSolve(_solveResult);
            _checkBtn.interactable = issues.Count == 0 && _solveTask == null;
            _undoBtn.interactable = _undo.Count > 0;
            _redoBtn.interactable = _redo.Count > 0;
        }

        private static string DescribeSolve(SolveResult r)
        {
            switch (r.status)
            {
                case SolveStatus.Solved: return "<color=#4CB86A>√ 可解</color>  推动 " + r.pushes + " 次 · 参考 " + r.moves + " 步";
                case SolveStatus.Unsolvable: return "<color=#E0605E>× 无解</color>  该布局无法完成";
                case SolveStatus.LimitReached: return "<color=#F2C14E>? 过于复杂</color>  求解器未能在限定步数内完成";
                case SolveStatus.Cancelled: return "已取消";
                default: return "<color=#E0605E>结构无效</color> " + r.message;
            }
        }

        // ------------------------------------------------------------------ solver

        private void StartSolve(bool thenSave)
        {
            if (_solveTask != null) return;
            if (LevelValidator.Validate(_work).Count > 0) return;
            _saveAfterSolve = thenSave;
            var snapshot = _work.Clone();
            _solveCancel = false;
            _solveVersion = _version;
            _solveTask = Task.Run(() => Solver.SolveAuto(snapshot, 1500000, () => _solveCancel));
            _solveModal = Modal.Create("正在验证可解性", 640, 360);
            _solveModal.AddMessage("求解器正在搜索解法，复杂关卡可能需要几秒钟…");
            _solveModal.AddButton(new ModalButton("取消", CancelSolve, Theme.Button));
            _solveModal.OnEscape = () => { _solveModal.Close(); CancelSolve(); };
            RefreshInfo();
        }

        private void CancelSolve()
        {
            _solveCancel = true;
            _solveTask = null;
            _saveAfterSolve = false;
            if (_solveModal != null) { _solveModal.Close(); _solveModal = null; }
            if (_checkBtn != null) RefreshInfo();
        }

        private void PollSolve()
        {
            if (_solveTask == null || !_solveTask.IsCompleted) return;
            var task = _solveTask;
            _solveTask = null;
            if (_solveModal != null) { _solveModal.Close(); _solveModal = null; }
            bool thenSave = _saveAfterSolve;
            _saveAfterSolve = false;
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                App.ShowToast("求解出错");
                RefreshInfo();
                return;
            }
            if (_solveVersion != _version) { RefreshInfo(); return; }
            _solveResult = task.Result;
            _solveResultVersion = _version;
            RefreshInfo();

            if (!thenSave)
            {
                if (_solveResult.status == SolveStatus.Solved) App.Sfx.Play(SfxId.BoxOnGoal);
                else App.Sfx.Play(SfxId.Error, 0.6f);
                return;
            }
            switch (_solveResult.status)
            {
                case SolveStatus.Solved:
                    DoSave(_solveResult.moves);
                    break;
                case SolveStatus.Unsolvable:
                    Modal.Message("关卡无解", "求解器确认该关卡无法完成。\n仍然保存的话，玩家将无法通关。",
                        new ModalButton("返回修改", null, Theme.Button),
                        new ModalButton("仍然保存", () => DoSave(0), Theme.Danger));
                    break;
                default:
                    DoSave(0);
                    App.ShowToast("已保存（关卡较复杂，未能自动验证可解性）", 3f);
                    break;
            }
        }

        // ------------------------------------------------------------------ save / load / io

        private void SaveFlow(bool saveAsNew)
        {
            if (saveAsNew) _editingId = null;
            _work.title = string.IsNullOrEmpty(_name.text) ? "未命名关卡" : _name.text.Trim();
            var issues = LevelValidator.Validate(_work);
            if (issues.Count > 0)
            {
                Modal.Message("关卡存在问题", string.Join("\n", issues.ToArray()) + "\n\n可以先保存为草稿，修复后才能游玩。",
                    new ModalButton("返回修改", null, Theme.Button),
                    new ModalButton("保存草稿", () => DoSave(0), Theme.Accent));
                return;
            }
            if (_solveResult != null && _solveResultVersion == _version && _solveResult.status == SolveStatus.Solved)
            {
                DoSave(_solveResult.moves);
                return;
            }
            StartSolve(true);
        }

        private void DoSave(int par)
        {
            _work.par = par;
            _work.id = _editingId ?? "";
            var saved = App.Levels.SaveCustom(_work.Clone());
            _editingId = saved.id;
            _work.id = saved.id;
            _work.title = saved.title;
            _name.SetTextWithoutNotify(saved.title);
            App.Save.ClearProgress(saved.id); // layout may have changed; old records are meaningless
            _dirty = false;
            RefreshInfo();
            if (par > 0) App.ShowToast("已保存「" + saved.title + "」· 已验证可解（参考 " + par + " 步）");
            else App.ShowToast("已保存「" + saved.title + "」");
        }

        private void ConfirmNew()
        {
            if (_dirty) Modal.Confirm("新建关卡", "当前关卡有未保存的修改，确定新建吗？", "新建", NewLevel, true);
            else NewLevel();
        }

        private void ConfirmExit()
        {
            System.Action exit = () =>
            {
                _dirty = false;
                App.Show(App.Menu);
            };
            if (_dirty)
            {
                Modal.Message("离开编辑器", "当前关卡有未保存的修改。",
                    new ModalButton("取消", null, Theme.Button),
                    new ModalButton("不保存离开", exit, Theme.Danger),
                    new ModalButton("保存", () => SaveFlow(false), Theme.Accent));
            }
            else exit();
        }

        private void Playtest()
        {
            var issues = LevelValidator.Validate(_work);
            if (issues.Count > 0)
            {
                Modal.Message("无法试玩", string.Join("\n", issues.ToArray()));
                App.Sfx.Play(SfxId.Error, 0.6f);
                return;
            }
            var copy = _work.Clone();
            copy.title = string.IsNullOrEmpty(_name.text) ? "未命名关卡" : _name.text;
            copy.id = "__playtest__";
            if (_solveResult != null && _solveResultVersion == _version && _solveResult.status == SolveStatus.Solved)
                copy.par = _solveResult.moves;
            App.PlayTest(copy);
        }

        private void OpenDialog()
        {
            var m = Modal.Create("打开关卡", 900, 820);
            UIFactory.VLayout(m.Body, 0);
            RectTransform content;
            var scroll = UIFactory.Scroll(m.Body, out content);
            UIFactory.Layout(scroll, -1, 560, 1, 1);
            var v = UIFactory.VLayout(content, 10, new RectOffset(4, 4, 4, 4));
            v.childForceExpandHeight = false;

            var header = UIFactory.Label(content, "我的关卡（" + App.Levels.Custom.Count + "）", 26, Theme.AccentYellow, TextAnchor.MiddleLeft);
            UIFactory.Layout(header, -1, 40);
            if (App.Levels.Custom.Count == 0)
                UIFactory.Layout(UIFactory.Label(content, "还没有保存过关卡", 24, Theme.TextDim, TextAnchor.MiddleLeft), -1, 40);
            foreach (var lvl in App.Levels.Custom)
            {
                var l = lvl;
                var row = LevelRow(content, l);
                UIFactory.Button(row, "打开", () => { m.Close(); OpenForEdit(l); }, Theme.Accent, 24, 110, 56);
                UIFactory.Button(row, "删除", () => Modal.Confirm("删除关卡", "确定删除「" + l.title + "」？此操作不可撤销。", "删除", () =>
                {
                    App.Levels.DeleteCustom(l.id);
                    App.Save.ClearProgress(l.id);
                    if (_editingId == l.id) { _editingId = null; _dirty = true; RefreshInfo(); }
                    m.Close();
                    OpenDialog();
                }, true), Theme.Danger, 24, 110, 56);
            }

            var header2 = UIFactory.Label(content, "官方关卡（打开副本作为模板）", 26, Theme.AccentYellow, TextAnchor.MiddleLeft);
            UIFactory.Layout(header2, -1, 50);
            for (int i = 0; i < App.Levels.BuiltIn.Count; i++)
            {
                var l = App.Levels.BuiltIn[i];
                var row = LevelRow(content, l, (i + 1) + ". ");
                UIFactory.Button(row, "复制", () => { m.Close(); OpenForEdit(l); }, Theme.Button, 24, 110, 56);
            }
            m.AddButton(new ModalButton("关闭", null, Theme.Button));
        }

        private Transform LevelRow(Transform parent, LevelData l, string prefix = "")
        {
            var row = UIFactory.Panel(parent, Theme.PanelLight, "Row");
            UIFactory.Layout(row, -1, 72);
            UIFactory.HLayout(row, 12, new RectOffset(18, 12, 8, 8), TextAnchor.MiddleLeft, false);
            var t = UIFactory.Label(row.transform, prefix + l.title, 28, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Layout(t, -1, 56, 1);
            var info = UIFactory.Label(row.transform, l.width + "×" + l.height + " · " + l.CountBoxes() + " 箱", 22, Theme.TextDim, TextAnchor.MiddleRight);
            UIFactory.Layout(info, 170, 56);
            return row.transform;
        }

        private void ImportDialog()
        {
            var m = Modal.Create("导入关卡文本", 900, 820);
            UIFactory.VLayout(m.Body, 12);
            var hint = UIFactory.Label(m.Body, "粘贴标准推箱子文本格式（# 墙  @ 玩家  $ 箱子  . 目标  * 箱子在目标  + 玩家在目标）", 22, Theme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Layout(hint, -1, 60);
            var field = UIFactory.Input(m.Body, "Title: 我的关卡\n#####\n#@$.#\n#####", true, 26);
            field.textComponent.font = UIFactory.Font;
            UIFactory.Layout(field, -1, 480, 1, 1);
            string clip = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clip) && LevelParser.ParseSingle(clip) != null) field.text = clip;
            m.AddButton(new ModalButton("取消", null, Theme.Button));
            m.AddButton(new ModalButton("导入", () =>
            {
                var lvl = LevelParser.ParseSingle(field.text);
                if (lvl == null || lvl.width < 1)
                {
                    App.ShowToast("没有识别到关卡数据");
                    App.Sfx.Play(SfxId.Error, 0.6f);
                    return;
                }
                if (lvl.width > LevelValidator.MaxSize || lvl.height > LevelValidator.MaxSize)
                {
                    App.ShowToast("关卡过大（最大 " + LevelValidator.MaxSize + "×" + LevelValidator.MaxSize + "）");
                    return;
                }
                if (lvl.width < LevelValidator.MinSize || lvl.height < LevelValidator.MinSize)
                    lvl.Resize(Mathf.Max(lvl.width, LevelValidator.MinSize), Mathf.Max(lvl.height, LevelValidator.MinSize));
                lvl.id = "";
                m.Close();
                Load(lvl, null);
                _dirty = true;
                RefreshInfo();
                App.ShowToast("已导入" + (string.IsNullOrEmpty(lvl.title) ? "" : "「" + lvl.title + "」"));
            }, Theme.Accent, true));
        }

        private void Export()
        {
            var copy = _work.Clone();
            copy.title = _name.text;
            copy.id = "";
            GUIUtility.systemCopyBuffer = copy.ToXsb(true);
            App.ShowToast("关卡文本已复制到剪贴板");
        }

        // ------------------------------------------------------------------ random generation

        private void AskGenerate()
        {
            var m = Modal.Create("随机生成关卡", 1100, 460);
            m.AddMessage("基于“逆向拉箱”算法生成，并用求解器保证一定可解。\n选择箱子数量（尺寸约 8~11 格）：");
            for (int n = 2; n <= 5; n++)
            {
                int boxes = n;
                m.AddButton(new ModalButton(n + " 个箱子", () => Generate(boxes), n == 3 ? Theme.Accent : Theme.Button));
            }
        }

        private void Generate(int boxes)
        {
            if (_genTask != null) return;
            int size = 7 + boxes;
            var opt = new GeneratorOptions
            {
                width = size + 1,
                height = size,
                boxes = boxes,
                wallDensity = 0.28f,
                pullSteps = 500,
                attempts = boxes >= 5 ? 25 : 40,
                solverNodes = 120000,
                seed = System.Environment.TickCount
            };
            _genCancel = false;
            opt.isCancelled = () => _genCancel;
            _genSolve = null;
            _genTask = Task.Run(() =>
            {
                SolveResult r;
                var lvl = LevelGenerator.Generate(opt, out r);
                _genSolve = r;
                return lvl;
            });
            var modal = Modal.Create("正在生成…", 600, 320);
            modal.AddMessage("正在生成并验证关卡…");
            modal.AddButton(new ModalButton("取消", () => { _genCancel = true; _genTask = null; }, Theme.Button));
            modal.OnEscape = () => { _genCancel = true; _genTask = null; modal.Close(); };
            _genModal = modal;
        }

        private Modal _genModal;
        private SolveResult _genSolve;

        private void PollGenerate()
        {
            if (_genTask == null || !_genTask.IsCompleted) return;
            var task = _genTask;
            _genTask = null;
            if (_genModal != null) { _genModal.Close(); _genModal = null; }
            if (task.IsFaulted || task.Result == null)
            {
                if (task.IsFaulted) Debug.LogException(task.Exception);
                App.ShowToast("生成失败，请重试");
                return;
            }
            var lvl = task.Result;
            PushUndo();
            string title = _name.text;
            _work = lvl;
            _work.title = title;
            _work.id = _editingId ?? "";
            Changed(true);
            _solveResult = _genSolve;
            _solveResultVersion = _genSolve != null ? _version : -1;
            RefreshInfo();
            App.ShowToast("已生成新关卡（可撤销）");
        }

        // ------------------------------------------------------------------ per frame

        public override void Tick(bool inputBlocked)
        {
            PollSolve();
            PollGenerate();
            if (_work == null) return;
            if (inputBlocked)
            {
                _stroking = false;
                App.Board.SetHover(false, default(Int2), Color.white);
                return;
            }
            if (!UIFactory.IsTypingInInputField()) HandleKeys();
            HandleMouse();
        }

        private void HandleKeys()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                        Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetTool(EditorTool.Wall);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetTool(EditorTool.Floor);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetTool(EditorTool.Goal);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SetTool(EditorTool.Box);
            if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) SetTool(EditorTool.Player);
            if (Input.GetKeyDown(KeyCode.Z) && ctrl && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) Redo();
            else if (Input.GetKeyDown(KeyCode.Z) && ctrl) Undo();
            if (Input.GetKeyDown(KeyCode.Y) && ctrl) Redo();
            if (Input.GetKeyDown(KeyCode.S) && ctrl) SaveFlow(false);
            if (Input.GetKeyDown(KeyCode.F5) || (Input.GetKeyDown(KeyCode.P) && ctrl)) Playtest();
            if (Input.GetKeyDown(KeyCode.Escape)) ConfirmExit();
        }

        private void HandleMouse()
        {
            bool overUI = UIFactory.PointerOverUI();
            Int2 cell;
            bool inside = App.Board.WorldToCell(App.ScreenToWorld(Input.mousePosition), out cell);

            if (!_stroking && !overUI && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && inside)
            {
                _stroking = true;
                _strokeErase = Input.GetMouseButtonDown(1);
                _strokeChanged = false;
                PushUndo();
                _lastPainted = new Int2(-1, -1);
            }

            if (_stroking)
            {
                bool held = _strokeErase ? Input.GetMouseButton(1) : Input.GetMouseButton(0);
                if (!held)
                {
                    _stroking = false;
                    if (!_strokeChanged && _undo.Count > 0) _undo.RemoveAt(_undo.Count - 1); // nothing changed
                    else if (_strokeChanged) Changed(false);
                }
                else if (inside && cell != _lastPainted)
                {
                    // interpolate to avoid gaps on fast drags
                    if (_lastPainted.x >= 0) PaintLine(_lastPainted, cell);
                    else if (Paint(cell, _tool, _strokeErase)) _strokeChanged = true;
                    _lastPainted = cell;
                    if (_strokeChanged) App.Board.RefreshEditor(_work);
                    RefreshInfoLight();
                }
            }

            if (inside && !overUI)
            {
                Color c = _strokeErase && _stroking ? new Color(1f, 0.45f, 0.45f, 0.8f) : new Color(1f, 1f, 1f, 0.6f);
                App.Board.SetHover(true, cell, c);
            }
            else App.Board.SetHover(false, cell, Color.white);
        }

        private void PaintLine(Int2 a, Int2 b)
        {
            if (_tool == EditorTool.Player && !_strokeErase)
            {
                if (Paint(b, _tool, false)) _strokeChanged = true;
                return;
            }
            int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = new Int2(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t)));
                if (Paint(p, _tool, _strokeErase)) _strokeChanged = true;
            }
        }

        private void RefreshInfoLight()
        {
            if (!_strokeChanged) return;
            _dirty = true;
            _version++;
            RefreshInfo();
        }
    }
}
