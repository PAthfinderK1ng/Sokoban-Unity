using Sokoban.Game.UI;
using UnityEngine;

namespace Sokoban.Game.Screens
{
    public enum PlayContext
    {
        BuiltIn,
        Custom,
        Playtest
    }

    public abstract class ScreenBase
    {
        protected App App;
        public RectTransform Root { get; private set; }

        /// <summary>Whether the world-space board is visible behind this screen.</summary>
        public virtual bool ShowsBoard { get { return false; } }

        /// <summary>Screen area covered by UI (left, top, right, bottom) in 1920x1080 canvas units.</summary>
        public virtual Vector4 BoardInsets { get { return Vector4.zero; } }

        public bool CameraDirty { get; set; }

        public void Build(App app, RectTransform layer)
        {
            App = app;
            Root = UIFactory.Stretch(UIFactory.Rect(GetType().Name, layer));
            OnBuild();
            Root.gameObject.SetActive(false);
        }

        public void Show()
        {
            Root.gameObject.SetActive(true);
            OnShow();
        }

        public void Hide()
        {
            OnHide();
            Root.gameObject.SetActive(false);
        }

        public bool IsVisible { get { return Root != null && Root.gameObject.activeSelf; } }

        protected abstract void OnBuild();
        protected virtual void OnShow() { }
        protected virtual void OnHide() { }

        /// <summary>Per-frame update. inputBlocked = a modal dialog is open.</summary>
        public virtual void Tick(bool inputBlocked) { }

        // ------------------------------------------------------------------ shared building blocks

        protected Transform TopBar(string title, System.Action onBack, float height = 110)
        {
            var bar = UIFactory.Panel(Root, Theme.PanelBg, "TopBar", false);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, height);
            rt.anchoredPosition = Vector2.zero;
            var h = UIFactory.HLayout(bar, 20, new RectOffset(30, 30, 18, 18), TextAnchor.MiddleLeft);
            h.childForceExpandHeight = false;
            if (onBack != null) UIFactory.Button(bar.transform, "← 返回", onBack, Theme.Button, 30, 170, 72);
            var t = UIFactory.Label(bar.transform, title, 44, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Layout(t, -1, 72, 1);
            return bar.transform;
        }

        protected static string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        protected static string Stars(int n, int max = 3)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max; i++) sb.Append(i < n ? "★" : "☆");
            return sb.ToString();
        }
    }
}
