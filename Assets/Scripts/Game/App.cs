using Sokoban.Core;
using Sokoban.Game.Screens;
using Sokoban.Game.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban.Game
{
    /// <summary>
    /// Composition root. Lives in Main.unity (and is auto-created if the scene is missing it),
    /// builds camera, UI and services, and routes between screens.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class App : MonoBehaviour
    {
        public static App I { get; private set; }

        public SpriteFactory Art { get; private set; }
        public SfxPlayer Sfx { get; private set; }
        public SaveSystem Save { get; private set; }
        public LevelRepository Levels { get; private set; }
        public BoardView Board { get; private set; }
        public Camera Cam { get; private set; }
        public Canvas Canvas { get; private set; }
        public RectTransform ScreenLayer { get; private set; }

        public MainMenuScreen Menu { get; private set; }
        public LevelSelectScreen Select { get; private set; }
        public GameScreen Gameplay { get; private set; }
        public EditorScreen Editor { get; private set; }
        public SettingsScreen Settings { get; private set; }

        private ScreenBase _current;
        private Toast _toast;
        private Vector2Int _lastScreenSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (I == null && FindAnyObjectByType<App>() == null)
                new GameObject("App").AddComponent<App>();
        }

        private void Awake()
        {
            if (I != null && I != this)
            {
                Destroy(gameObject);
                return;
            }
            I = this;
            Application.targetFrameRate = 60;
            Input.multiTouchEnabled = false;

            Art = new SpriteFactory();
            UIFactory.Init(Art);
            Save = new SaveSystem();
            Levels = new LevelRepository();
            Sfx = new SfxPlayer(gameObject);
            UIFactory.OnClickSound = () => Sfx.Play(SfxId.Click, 0.6f);

            BuildCamera();
            BuildBoard();
            BuildUI();
            ApplySettings();

            Menu = new MainMenuScreen();
            Select = new LevelSelectScreen();
            Gameplay = new GameScreen();
            Editor = new EditorScreen();
            Settings = new SettingsScreen();
            foreach (var s in new ScreenBase[] { Menu, Select, Gameplay, Editor, Settings }) s.Build(this, ScreenLayer);

            Show(Menu);
        }

        private void BuildCamera()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                Cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            else if (FindAnyObjectByType<AudioListener>() == null)
            {
                Cam.gameObject.AddComponent<AudioListener>();
            }
            Cam.orthographic = true;
            Cam.orthographicSize = 6;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Theme.Background;
            Cam.transform.position = new Vector3(0, 0, -10);
        }

        private void BuildBoard()
        {
            var go = new GameObject("Board");
            Board = go.AddComponent<BoardView>();
            Board.Art = Art;
            Board.SetVisible(false);
        }

        private void BuildUI()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var cgo = new GameObject("Canvas");
            cgo.layer = 5;
            Canvas = cgo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.pixelPerfect = false;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Expand: the canvas is always at least 1920x1080 units, so layouts never get clipped.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            cgo.AddComponent<GraphicRaycaster>();

            ScreenLayer = UIFactory.Stretch(UIFactory.Rect("Screens", cgo.transform));
            var modalLayer = UIFactory.Stretch(UIFactory.Rect("Modals", cgo.transform));
            Modal.Layer = modalLayer;
            var toastLayer = UIFactory.Stretch(UIFactory.Rect("Toasts", cgo.transform));
            _toast = Toast.Create(toastLayer);
        }

        // ------------------------------------------------------------------ navigation

        public ScreenBase Current { get { return _current; } }

        public void Show(ScreenBase screen)
        {
            Modal.CloseAll();
            if (_current != null && _current != screen) _current.Hide();
            _current = screen;
            _current.Show();
            Board.SetVisible(_current.ShowsBoard);
            FitCamera(true);
        }

        public void ShowToast(string message, float seconds = 2.2f)
        {
            _toast.Show(message, seconds);
        }

        public void PlayBuiltIn(int index)
        {
            if (index < 0 || index >= Levels.BuiltIn.Count) return;
            Show(Gameplay);
            Gameplay.StartLevel(Levels.BuiltIn[index], PlayContext.BuiltIn, index);
        }

        public void PlayCustom(LevelData level)
        {
            Show(Gameplay);
            Gameplay.StartLevel(level, PlayContext.Custom, Levels.Custom.IndexOf(level));
        }

        public void PlayTest(LevelData level)
        {
            Show(Gameplay);
            Gameplay.StartLevel(level, PlayContext.Playtest, -1);
        }

        public bool IsBuiltInUnlocked(int index)
        {
            if (Save.Data.unlockAll || index <= 0) return true;
            return Save.IsCompleted(Levels.BuiltIn[index - 1].id) || Save.IsCompleted(Levels.BuiltIn[index].id);
        }

        /// <summary>First unlocked, not yet completed built-in level (or the last one).</summary>
        public int NextBuiltInToPlay()
        {
            for (int i = 0; i < Levels.BuiltIn.Count; i++)
                if (!Save.IsCompleted(Levels.BuiltIn[i].id)) return i;
            return Levels.BuiltIn.Count - 1;
        }

        public void ApplySettings()
        {
            var d = Save.Data;
            Sfx.Enabled = d.sfxEnabled;
            Sfx.Volume = d.volume;
            float[] speeds = { 0.17f, 0.11f, 0.06f };
            Board.MoveDuration = speeds[Mathf.Clamp(d.animSpeed, 0, 2)];
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            bool modal = Modal.HandleEscape();
            if (_current != null) _current.Tick(modal);
            FitCamera(false);
        }

        /// <summary>Fits the board into the screen area not covered by the current screen's UI.</summary>
        public void FitCamera(bool force)
        {
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && size == _lastScreenSize && !(_current != null && _current.CameraDirty)) return;
            _lastScreenSize = size;
            if (_current != null) _current.CameraDirty = false;
            if (_current == null || !_current.ShowsBoard || Board.Width == 0) return;

            Vector4 insets = _current.BoardInsets; // left, top, right, bottom in canvas units
            // CanvasScaler Expand mode: scale = min(w/1920, h/1080)
            float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            if (scale <= 0) scale = 1;
            float left = insets.x * scale, top = insets.y * scale, right = insets.z * scale, bottom = insets.w * scale;
            float availW = Mathf.Max(100, Screen.width - left - right);
            float availH = Mathf.Max(100, Screen.height - top - bottom);
            float margin = 0.8f;
            float bw = Board.Width + margin, bh = Board.Height + margin;
            float ortho = Mathf.Max(bw * Screen.height / (2f * availW), bh * Screen.height / (2f * availH));
            ortho = Mathf.Max(ortho, 3f);
            Cam.orthographicSize = ortho;
            float unitsPerPixel = 2f * ortho / Screen.height;
            float cx = left + availW * 0.5f - Screen.width * 0.5f;
            float cy = bottom + availH * 0.5f - Screen.height * 0.5f;
            Cam.transform.position = new Vector3(-cx * unitsPerPixel, -cy * unitsPerPixel, -10);
        }

        public Vector3 ScreenToWorld(Vector3 screenPos)
        {
            var w = Cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 10));
            w.z = 0;
            return w;
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
