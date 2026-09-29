using Sokoban.Core;
using Sokoban.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game.Screens
{
    public class LevelSelectScreen : ScreenBase
    {
        private bool _customTab;
        private Button _tabBuiltIn, _tabCustom;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Text _summary;

        protected override void OnBuild()
        {
            var bg = UIFactory.Panel(Root, Theme.Background, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);
            var bar = TopBar("选择关卡", () => App.Show(App.Menu));
            _summary = UIFactory.Label(bar, "", 28, Theme.TextDim, TextAnchor.MiddleRight);
            UIFactory.Layout(_summary, 520, 72);

            var tabs = UIFactory.Rect("Tabs", Root);
            UIFactory.Place(tabs, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(1200, 76));
            UIFactory.HLayout(tabs, 16, null, TextAnchor.MiddleCenter);
            _tabBuiltIn = UIFactory.Button(tabs, "官方关卡", () => SetTab(false), Theme.Button, 32, 280, 72);
            _tabCustom = UIFactory.Button(tabs, "自定义关卡", () => SetTab(true), Theme.Button, 32, 280, 72);

            _scroll = UIFactory.Scroll(Root, out _content);
            UIFactory.Stretch((RectTransform)_scroll.transform, 120, 230, 120, 40);
            var grid = _content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(300, 220);
            grid.spacing = new Vector2(28, 28);
            grid.padding = new RectOffset(10, 10, 10, 30);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
        }

        protected override void OnShow()
        {
            Refresh();
        }

        public void SetTab(bool custom)
        {
            _customTab = custom;
            Refresh();
        }

        public void ShowCustomTab()
        {
            _customTab = true;
            App.Show(this);
        }

        private void Refresh()
        {
            UIFactory.SetButtonColor(_tabBuiltIn, _customTab ? Theme.Button : Theme.ButtonSelected);
            UIFactory.SetButtonColor(_tabCustom, _customTab ? Theme.ButtonSelected : Theme.Button);
            for (int i = _content.childCount - 1; i >= 0; i--) Object.Destroy(_content.GetChild(i).gameObject);

            if (!_customTab)
            {
                int done = 0, stars = 0;
                for (int i = 0; i < App.Levels.BuiltIn.Count; i++)
                {
                    var l = App.Levels.BuiltIn[i];
                    var p = App.Save.Get(l.id);
                    if (p != null && p.completed) { done++; stars += p.stars; }
                    BuildCard(i, l, App.IsBuiltInUnlocked(i), false);
                }
                _summary.text = "已完成 " + done + "/" + App.Levels.BuiltIn.Count + "    ★ " + stars;
            }
            else
            {
                BuildNewCard();
                for (int i = 0; i < App.Levels.Custom.Count; i++) BuildCard(i, App.Levels.Custom[i], true, true);
                _summary.text = "共 " + App.Levels.Custom.Count + " 个自定义关卡";
            }
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void BuildCard(int index, LevelData level, bool unlocked, bool custom)
        {
            var p = App.Save.Get(level.id);
            bool completed = p != null && p.completed;
            bool playable = !custom || LevelValidator.Validate(level).Count == 0;

            Color bg = !unlocked ? Theme.Locked : (completed ? Theme.Hex("#2E4A3C") : Theme.PanelLight);
            var card = UIFactory.Button(_content, "", null, bg, 30, -1, -1);
            card.interactable = unlocked;
            Object.Destroy(card.transform.Find("Label").gameObject);
            var v = UIFactory.VLayout(card, 4, new RectOffset(14, 14, 14, 12), TextAnchor.UpperCenter);
            v.childForceExpandHeight = false;

            string head = custom ? "#" + (index + 1) : (index + 1).ToString();
            var num = UIFactory.Label(card.transform, unlocked ? head : "锁定", unlocked ? 54 : 40, unlocked ? Theme.Text : Theme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Layout(num, -1, 70);
            var title = UIFactory.Label(card.transform, level.title, 26, unlocked ? Theme.Text : Theme.TextDim);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Layout(title, -1, 36);

            string info;
            if (!unlocked) info = "完成上一关解锁";
            else if (custom && !playable) info = "<color=#E0A060>草稿 · 需修复后可玩</color>";
            else if (completed) info = "<color=#F2C14E>" + Stars(p.stars) + "</color>  最佳 " + p.bestMoves + " 步";
            else info = level.width + "×" + level.height + " · " + level.CountBoxes() + " 箱" + (level.par > 0 ? " · 参考 " + level.par + " 步" : "");
            var infoText = UIFactory.Label(card.transform, info, 24, Theme.TextDim);
            UIFactory.Layout(infoText, -1, 34);

            if (custom)
            {
                var row = UIFactory.Rect("Row", card.transform);
                UIFactory.HLayout(row, 10, null, TextAnchor.MiddleCenter);
                UIFactory.Layout(row, -1, 44);
                var lvl = level;
                UIFactory.Button(row, "编辑", () => App.Editor.OpenForEdit(lvl), Theme.Button, 22, 110, 44);
            }

            int idx = index;
            var lv = level;
            card.onClick.AddListener(() =>
            {
                if (!custom) App.PlayBuiltIn(idx);
                else if (playable) App.PlayCustom(lv);
                else App.Editor.OpenForEdit(lv);
            });
        }

        private void BuildNewCard()
        {
            UIFactory.Button(_content, "+\n<size=28>新建关卡</size>", () =>
            {
                App.Show(App.Editor);
                App.Editor.NewLevel();
            }, Theme.Hex("#2B3550"), 70, -1, -1);
        }

        public override void Tick(bool inputBlocked)
        {
            if (inputBlocked) return;
            if (Input.GetKeyDown(KeyCode.Escape)) App.Show(App.Menu);
            if (Input.GetKeyDown(KeyCode.Tab)) SetTab(!_customTab);
        }
    }
}
