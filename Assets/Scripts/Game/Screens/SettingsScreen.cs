using Sokoban.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game.Screens
{
    public class SettingsScreen : ScreenBase
    {
        private Button _sfx, _speed, _unlock, _fullscreen, _deadlock;
        private Text _volume;

        protected override void OnBuild()
        {
            var bg = UIFactory.Panel(Root, Theme.Background, "Bg", false);
            UIFactory.Stretch(bg.rectTransform);
            TopBar("设置", () => App.Show(App.Menu));

            var panel = UIFactory.Panel(Root, Theme.PanelLight, "Settings");
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(1000, 800));
            var v = UIFactory.VLayout(panel, 18, new RectOffset(50, 50, 40, 40));
            v.childForceExpandHeight = false;

            _sfx = Row(panel.transform, "音效", () => { App.Save.Data.sfxEnabled = !App.Save.Data.sfxEnabled; Commit(); });

            var volRow = RowRoot(panel.transform, "音量");
            UIFactory.Button(volRow, "－", () => { App.Save.Data.volume = Mathf.Clamp01(App.Save.Data.volume - 0.1f); Commit(); }, Theme.Button, 36, 90, 64);
            _volume = UIFactory.Label(volRow, "", 32, Theme.Text);
            UIFactory.Layout(_volume, 120, 64);
            UIFactory.Button(volRow, "＋", () => { App.Save.Data.volume = Mathf.Clamp01(App.Save.Data.volume + 0.1f); Commit(); App.Sfx.Play(SfxId.BoxOnGoal); }, Theme.Button, 36, 90, 64);

            _speed = Row(panel.transform, "移动动画速度", () => { App.Save.Data.animSpeed = (App.Save.Data.animSpeed + 1) % 3; Commit(); });
            _deadlock = Row(panel.transform, "死局提醒", () => { App.Save.Data.showDeadlockWarnings = !App.Save.Data.showDeadlockWarnings; Commit(); });
            _fullscreen = Row(panel.transform, "全屏", () => { _pendingFullscreen = !_pendingFullscreen; Screen.fullScreen = _pendingFullscreen; Refresh(); });
            _unlock = Row(panel.transform, "解锁全部关卡", () => { App.Save.Data.unlockAll = !App.Save.Data.unlockAll; Commit(); });

            var resetRow = RowRoot(panel.transform, "游戏进度");
            UIFactory.Button(resetRow, "重置进度", () => Modal.Confirm("重置进度", "将清除所有关卡的通关记录与星级，此操作不可撤销。", "重置", () =>
            {
                App.Save.ResetAllProgress();
                App.ShowToast("进度已重置");
            }, true), Theme.Danger, 30, 300, 64);

            var help = UIFactory.Label(panel.transform,
                "操作：方向键/WASD 移动 · 鼠标点击地面自动寻路 · 点击相邻箱子推动\nZ/退格 撤销 · Y 重做 · R 重开 · H 提示 · Esc 暂停",
                24, Theme.TextDim);
            UIFactory.Layout(help, -1, 90);
        }

        private bool _pendingFullscreen;

        private Transform RowRoot(Transform parent, string label)
        {
            var row = UIFactory.Rect("Row", parent);
            UIFactory.HLayout(row, 16, null, TextAnchor.MiddleRight, false);
            UIFactory.Layout(row, -1, 70);
            var t = UIFactory.Label(row, label, 32, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Layout(t, -1, 64, 1);
            return row;
        }

        private Button Row(Transform parent, string label, System.Action onClick)
        {
            var row = RowRoot(parent, label);
            return UIFactory.Button(row, "", onClick, Theme.Button, 30, 300, 64);
        }

        private void Commit()
        {
            App.Save.Save();
            App.ApplySettings();
            Refresh();
        }

        protected override void OnShow()
        {
            _pendingFullscreen = Screen.fullScreen;
            Refresh();
        }

        private void Refresh()
        {
            var d = App.Save.Data;
            SetToggle(_sfx, d.sfxEnabled);
            SetToggle(_deadlock, d.showDeadlockWarnings);
            SetToggle(_unlock, d.unlockAll);
            SetToggle(_fullscreen, _pendingFullscreen);
            _volume.text = Mathf.RoundToInt(d.volume * 100) + "%";
            string[] speeds = { "慢", "标准", "快" };
            UIFactory.SetButtonText(_speed, speeds[Mathf.Clamp(d.animSpeed, 0, 2)]);
        }

        private static void SetToggle(Button b, bool on)
        {
            UIFactory.SetButtonText(b, on ? "开" : "关");
            UIFactory.SetButtonColor(b, on ? Theme.AccentGreen : Theme.Button);
        }

        public override void Tick(bool inputBlocked)
        {
            if (!inputBlocked && Input.GetKeyDown(KeyCode.Escape)) App.Show(App.Menu);
        }
    }
}
