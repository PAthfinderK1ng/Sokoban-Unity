using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban.Game.UI
{
    /// <summary>
    /// Builds uGUI hierarchies from code, so the whole game lives in scripts
    /// (no prefabs to break, easy to review in a pull request).
    /// </summary>
    public static class UIFactory
    {
        public static Font Font;
        public static SpriteFactory Art;
        public static Action OnClickSound;

        public static void Init(SpriteFactory art)
        {
            Art = art;
            // Prefer an OS font with CJK glyphs; fall back to Unity's built-in font.
            try
            {
                Font = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Hiragino Sans GB",
                    "Noto Sans CJK SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "SimHei", "Arial"
                }, 32);
            }
            catch (Exception)
            {
                Font = null;
            }
            if (Font == null) Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ------------------------------------------------------------------ rect helpers

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchor at a single point (x,y in 0..1) with given pivot, position and size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static LayoutElement Layout(Component c, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = prefW;
            le.preferredHeight = prefH;
            le.flexibleWidth = flexW;
            le.flexibleHeight = flexH;
            if (prefW >= 0) le.minWidth = prefW;
            if (prefH >= 0) le.minHeight = prefH;
            return le;
        }

        public static VerticalLayoutGroup VLayout(Component c, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperCenter, bool expandWidth = true)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(Component c, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleCenter, bool expandHeight = true)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = expandHeight;
            return h;
        }

        // ------------------------------------------------------------------ widgets

        public static Image Panel(Transform parent, Color color, string name = "Panel", bool rounded = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (rounded)
            {
                img.sprite = Art.Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1f;
            }
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, Color? color = null, int fontSize = 30, float width = -1, float height = 72)
        {
            var img = Panel(parent, color ?? Theme.Button, "Button:" + text);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            // no keyboard navigation: arrow keys are used for gameplay
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            var label = Label(img.transform, text, fontSize, Theme.Text);
            Stretch(label.rectTransform, 12, 4, 12, 4);
            label.name = "Label";

            btn.onClick.AddListener(() =>
            {
                if (OnClickSound != null) OnClickSound();
                if (onClick != null) onClick();
            });
            img.gameObject.AddComponent<ButtonFx>();
            if (width > 0 || height > 0) Layout(img, width, height);
            return btn;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static void SetButtonColor(Button b, Color c)
        {
            var img = b.targetGraphic as Image;
            if (img != null) img.color = c;
        }

        public static InputField Input(Transform parent, string placeholder, bool multiline, int fontSize = 28)
        {
            var bg = Panel(parent, Theme.Hex("#1A1F2E"), "Input");
            var field = bg.gameObject.AddComponent<InputField>();
            var nav = field.navigation;
            nav.mode = Navigation.Mode.None;
            field.navigation = nav;

            var text = Label(bg.transform, "", fontSize, Theme.Text, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Stretch(text.rectTransform, 16, 8, 16, 8);
            var ph = Label(bg.transform, placeholder, fontSize, Theme.TextDim, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(ph.rectTransform, 16, 8, 16, 8);

            field.textComponent = text;
            field.placeholder = ph;
            field.targetGraphic = bg;
            if (multiline)
            {
                field.lineType = InputField.LineType.MultiLineNewline;
                text.font = Font;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Truncate;
            }
            else
            {
                field.lineType = InputField.LineType.SingleLine;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            return field;
        }

        /// <summary>Vertical scroll view. Returns the ScrollRect; `content` receives children (auto-sized).</summary>
        public static ScrollRect Scroll(Transform parent, out RectTransform content)
        {
            var root = Rect("Scroll", parent);
            var sr = root.gameObject.AddComponent<ScrollRect>();
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.001f); // needed for drag raycasts

            var viewport = Stretch(Rect("Viewport", root), 0, 0, 18, 0);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // scrollbar
            var barRt = Rect("Scrollbar", root);
            barRt.anchorMin = new Vector2(1, 0);
            barRt.anchorMax = new Vector2(1, 1);
            barRt.pivot = new Vector2(1, 0.5f);
            barRt.sizeDelta = new Vector2(10, 0);
            barRt.anchoredPosition = Vector2.zero;
            var barBg = barRt.gameObject.AddComponent<Image>();
            barBg.sprite = Art.Rounded;
            barBg.type = Image.Type.Sliced;
            barBg.pixelsPerUnitMultiplier = 4f;
            barBg.color = new Color(1, 1, 1, 0.05f);
            var bar = barRt.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            var slide = Stretch(Rect("Sliding", barRt));
            var handle = Stretch(Rect("Handle", slide));
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.sprite = Art.Rounded;
            hImg.type = Image.Type.Sliced;
            hImg.pixelsPerUnitMultiplier = 4f;
            hImg.color = new Color(1, 1, 1, 0.25f);
            bar.handleRect = handle;
            bar.targetGraphic = hImg;
            var nav = bar.navigation;
            nav.mode = Navigation.Mode.None;
            bar.navigation = nav;

            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            sr.verticalScrollbar = bar;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            sr.verticalScrollbarSpacing = 4;
            return sr;
        }

        public static Image Icon(Transform parent, Sprite sprite, float size)
        {
            var rt = Rect("Icon", parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            Layout(img, size, size);
            rt.sizeDelta = new Vector2(size, size);
            return img;
        }

        public static RectTransform Spacer(Transform parent, float w = -1, float h = -1, bool flexible = false)
        {
            var rt = Rect("Spacer", parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = h;
            if (flexible)
            {
                le.flexibleWidth = 1;
                le.flexibleHeight = 1;
            }
            return rt;
        }

        public static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var f = es.currentSelectedGameObject.GetComponent<InputField>();
            return f != null && f.isFocused;
        }

        public static bool PointerOverUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }
    }

    /// <summary>Small hover scale effect for buttons.</summary>
    public class ButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private float _target = 1f;
        private Selectable _sel;

        private void Awake() { _sel = GetComponent<Selectable>(); }

        public void OnPointerEnter(PointerEventData e)
        {
            if (_sel == null || _sel.interactable) _target = 1.04f;
        }

        public void OnPointerExit(PointerEventData e) { _target = 1f; }

        private void OnDisable()
        {
            _target = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            float s = Mathf.MoveTowards(transform.localScale.x, _target, Time.unscaledDeltaTime * 1.5f);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
