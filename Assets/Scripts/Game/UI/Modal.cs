using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Game.UI
{
    public struct ModalButton
    {
        public string label;
        public Action action;
        public Color color;
        public bool keepOpen;

        public ModalButton(string label, Action action, Color color, bool keepOpen = false)
        {
            this.label = label;
            this.action = action;
            this.color = color;
            this.keepOpen = keepOpen;
        }
    }

    /// <summary>Modal dialog with dimmed overlay. Only the top-most modal receives Esc.</summary>
    public class Modal
    {
        private static readonly List<Modal> Stack = new List<Modal>();
        public static Transform Layer;

        public RectTransform Root { get; private set; }
        public RectTransform Window { get; private set; }
        public RectTransform Body { get; private set; }
        public RectTransform ButtonRow { get; private set; }
        public Action OnEscape;
        public Action OnClosed;

        public static bool AnyOpen { get { return Stack.Count > 0; } }
        public static Modal Top { get { return Stack.Count > 0 ? Stack[Stack.Count - 1] : null; } }

        public static Modal Create(string title, float width, float height)
        {
            var m = new Modal();
            var overlay = UIFactory.Panel(Layer, Theme.Overlay, "Modal", false);
            m.Root = UIFactory.Stretch(overlay.rectTransform);

            var win = UIFactory.Panel(m.Root, Theme.PanelBgSolid, "Window");
            m.Window = UIFactory.Place(win.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var outline = UIFactory.Panel(win.transform, new Color(1, 1, 1, 0.08f), "Outline");
            outline.sprite = UIFactory.Art.RoundedOutline;
            outline.raycastTarget = false;
            outline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Stretch(outline.rectTransform);

            var v = UIFactory.VLayout(win, 18, new RectOffset(40, 40, 32, 32));
            v.childForceExpandHeight = false;

            var t = UIFactory.Label(win.transform, title, 40, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Layout(t, -1, 56);

            m.Body = UIFactory.Rect("Body", win.transform);
            UIFactory.Layout(m.Body, -1, -1, 1, 1);

            m.ButtonRow = UIFactory.Rect("Buttons", win.transform);
            UIFactory.HLayout(m.ButtonRow, 20, null, TextAnchor.MiddleCenter);
            UIFactory.Layout(m.ButtonRow, -1, 76);

            m.OnEscape = m.Close;
            Stack.Add(m);
            m.Window.localScale = Vector3.one * 0.92f;
            m.Root.gameObject.AddComponent<PopIn>();
            return m;
        }

        public Button AddButton(ModalButton b)
        {
            var btn = UIFactory.Button(ButtonRow, b.label, () =>
            {
                if (!b.keepOpen) Close();
                if (b.action != null) b.action();
            }, b.color, 30, 220, 72);
            return btn;
        }

        public Text AddMessage(string message, int size = 30)
        {
            UIFactory.VLayout(Body, 10, null, TextAnchor.MiddleCenter);
            var t = UIFactory.Label(Body, message, size, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Layout(t, -1, -1, 1, 1);
            return t;
        }

        public void Close()
        {
            if (Root == null) return;
            Stack.Remove(this);
            UnityEngine.Object.Destroy(Root.gameObject);
            Root = null;
            if (OnClosed != null) OnClosed();
        }

        public static void CloseAll()
        {
            for (int i = Stack.Count - 1; i >= 0; i--) Stack[i].Close();
        }

        /// <summary>Called by App every frame; routes Escape to the top-most modal.</summary>
        public static bool HandleEscape()
        {
            if (Stack.Count == 0) return false;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                var top = Stack[Stack.Count - 1];
                if (top.OnEscape != null) top.OnEscape();
            }
            return true;
        }

        // ------------------------------------------------------------------ presets

        public static Modal Message(string title, string message, params ModalButton[] buttons)
        {
            int lines = 1;
            foreach (char c in message) if (c == '\n') lines++;
            float h = 300 + Mathf.Max(0, lines - 1) * 40 + Mathf.Max(0, message.Length / 26) * 36;
            var m = Create(title, 840, Mathf.Min(h, 900));
            m.AddMessage(message);
            if (buttons == null || buttons.Length == 0)
                buttons = new[] { new ModalButton("确定", null, Theme.Accent) };
            foreach (var b in buttons) m.AddButton(b);
            return m;
        }

        public static Modal Confirm(string title, string message, string okLabel, Action onOk, bool danger = false)
        {
            return Message(title, message,
                new ModalButton("取消", null, Theme.Button),
                new ModalButton(okLabel, onOk, danger ? Theme.Danger : Theme.Accent));
        }
    }

    /// <summary>Scale-in animation for modal windows.</summary>
    public class PopIn : MonoBehaviour
    {
        private float _t;
        private Transform _win;

        private void Start() { _win = transform.Find("Window"); }

        private void Update()
        {
            if (_win == null) { enabled = false; return; }
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime * 7f);
            float s = Mathf.Lerp(0.92f, 1f, 1f - (1f - _t) * (1f - _t));
            _win.localScale = new Vector3(s, s, 1f);
            if (_t >= 1f) enabled = false;
        }
    }

    /// <summary>Transient notification at the bottom of the screen.</summary>
    public class Toast : MonoBehaviour
    {
        private Text _text;
        private CanvasGroup _group;
        private float _timer;

        public static Toast Create(Transform layer)
        {
            var bg = UIFactory.Panel(layer, new Color(0.08f, 0.09f, 0.14f, 0.92f), "Toast");
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(900, 76));
            bg.raycastTarget = false;
            var toast = bg.gameObject.AddComponent<Toast>();
            toast._group = bg.gameObject.AddComponent<CanvasGroup>();
            toast._group.blocksRaycasts = false;
            toast._group.alpha = 0;
            toast._text = UIFactory.Label(bg.transform, "", 30, Theme.Text);
            UIFactory.Stretch(toast._text.rectTransform, 20, 6, 20, 6);
            var fit = bg.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var h = UIFactory.HLayout(bg, 0, new RectOffset(36, 36, 14, 14));
            h.childForceExpandHeight = true;
            return toast;
        }

        public void Show(string message, float seconds = 2.2f)
        {
            _text.text = message;
            _timer = seconds;
            transform.SetAsLastSibling();
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            float target = _timer > 0 ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 5f);
        }
    }
}
