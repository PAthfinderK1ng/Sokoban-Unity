using Sokoban.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game.Screens
{
    public class MainMenuScreen : ScreenBase
    {
        private Text _progress;
        private Button _continue;

        protected override void OnBuild()
        {
            var bg = UIFactory.Panel(Root, Theme.Background, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);

            // decorative crates
            var deco = UIFactory.Stretch(UIFactory.Rect("Deco", Root));
            AddDeco(deco, App.Art.Box, new Vector2(0.14f, 0.72f), 150, -8);
            AddDeco(deco, App.Art.BoxOnGoal, new Vector2(0.86f, 0.30f), 170, 6);
            AddDeco(deco, App.Art.Goal, new Vector2(0.82f, 0.74f), 120, 0);
            AddDeco(deco, App.Art.Player[3], new Vector2(0.18f, 0.28f), 140, 0);
            AddDeco(deco, App.Art.Wall, new Vector2(0.08f, 0.12f), 110, 0);
            AddDeco(deco, App.Art.Wall, new Vector2(0.93f, 0.9f), 110, 0);

            var col = UIFactory.Rect("Column", Root);
            UIFactory.Place(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(620, 900));
            UIFactory.VLayout(col, 22, null, TextAnchor.MiddleCenter);

            var title = UIFactory.Label(col, "推 箱 子", 120, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Layout(title, -1, 150);
            var sub = UIFactory.Label(col, "S O K O B A N", 34, Theme.AccentYellow);
            UIFactory.Layout(sub, -1, 50);
            _progress = UIFactory.Label(col, "", 28, Theme.TextDim);
            UIFactory.Layout(_progress, -1, 60);

            _continue = UIFactory.Button(col, "开始游戏", () => App.PlayBuiltIn(App.NextBuiltInToPlay()), Theme.Accent, 36, -1, 88);
            UIFactory.Button(col, "选择关卡", () => App.Show(App.Select), Theme.Button, 34, -1, 80);
            UIFactory.Button(col, "关卡编辑器", () => { App.Show(App.Editor); App.Editor.NewLevelIfEmpty(); }, Theme.Button, 34, -1, 80);
            UIFactory.Button(col, "设置", () => App.Show(App.Settings), Theme.Button, 34, -1, 80);
            UIFactory.Button(col, "退出游戏", () => App.Quit(), Theme.Button, 34, -1, 80);

            var ver = UIFactory.Label(Root, "方向键 / WASD 移动   Z 撤销   R 重开   Esc 菜单", 24, Theme.TextDim);
            UIFactory.Place(ver.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1400, 40));
        }

        private static void AddDeco(Transform parent, Sprite s, Vector2 anchor, float size, float rot)
        {
            var img = UIFactory.Icon(parent, s, size);
            UIFactory.Place(img.rectTransform, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            img.rectTransform.localEulerAngles = new Vector3(0, 0, rot);
            img.color = new Color(1, 1, 1, 0.35f);
        }

        protected override void OnShow()
        {
            int done = 0, stars = 0, total = App.Levels.BuiltIn.Count;
            foreach (var l in App.Levels.BuiltIn)
            {
                var p = App.Save.Get(l.id);
                if (p != null && p.completed)
                {
                    done++;
                    stars += p.stars;
                }
            }
            _progress.text = "进度  " + done + " / " + total + " 关     ★ " + stars + " / " + total * 3;
            UIFactory.SetButtonText(_continue, done == 0 ? "开始游戏" : (done >= total ? "重玩最后一关" : "继续游戏 · 第 " + (App.NextBuiltInToPlay() + 1) + " 关"));
        }

        public override void Tick(bool inputBlocked)
        {
            if (inputBlocked) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                App.PlayBuiltIn(App.NextBuiltInToPlay());
        }
    }
}
