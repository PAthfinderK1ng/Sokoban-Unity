using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Game
{
    /// <summary>
    /// Built-in levels come from Resources/Levels/BuiltIn.txt.
    /// Custom levels created in the editor are stored as individual .xsb text files in
    /// persistentDataPath/CustomLevels (human-readable, easy to share).
    /// </summary>
    public class LevelRepository
    {
        public readonly List<LevelData> BuiltIn = new List<LevelData>();
        public readonly List<LevelData> Custom = new List<LevelData>();
        public string CustomFolder { get; private set; }

        public LevelRepository()
        {
            CustomFolder = Path.Combine(Application.persistentDataPath, "CustomLevels");
            LoadBuiltIn();
            LoadCustom();
        }

        private void LoadBuiltIn()
        {
            BuiltIn.Clear();
            var asset = Resources.Load<TextAsset>("Levels/BuiltIn");
            if (asset == null)
            {
                Debug.LogError("[Sokoban] Resources/Levels/BuiltIn.txt missing");
                return;
            }
            var list = LevelParser.ParseCollection(asset.text);
            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(list[i].id)) list[i].id = "builtin-" + (i + 1).ToString("00");
                if (string.IsNullOrEmpty(list[i].title)) list[i].title = "第 " + (i + 1) + " 关";
                BuiltIn.Add(list[i]);
            }
        }

        public void LoadCustom()
        {
            Custom.Clear();
            try
            {
                if (!Directory.Exists(CustomFolder)) Directory.CreateDirectory(CustomFolder);
                var files = Directory.GetFiles(CustomFolder, "*.xsb");
                Array.Sort(files, (a, b) => File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b)));
                foreach (var f in files)
                {
                    var lvl = LevelParser.ParseSingle(File.ReadAllText(f));
                    if (lvl == null) continue;
                    if (string.IsNullOrEmpty(lvl.id)) lvl.id = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrEmpty(lvl.title)) lvl.title = "未命名关卡";
                    Custom.Add(lvl);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Sokoban] Failed to load custom levels: " + e.Message);
            }
        }

        public LevelData FindCustom(string id)
        {
            foreach (var l in Custom) if (l.id == id) return l;
            return null;
        }

        public int IndexOfBuiltIn(string id)
        {
            for (int i = 0; i < BuiltIn.Count; i++) if (BuiltIn[i].id == id) return i;
            return -1;
        }

        public bool IsBuiltIn(LevelData l)
        {
            return l != null && IndexOfBuiltIn(l.id) >= 0;
        }

        /// <summary>Saves (creates or overwrites) a custom level. Assigns an id when missing.</summary>
        public LevelData SaveCustom(LevelData level)
        {
            if (string.IsNullOrEmpty(level.id) || IndexOfBuiltIn(level.id) >= 0)
                level.id = "custom-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + UnityEngine.Random.Range(100, 999);
            if (string.IsNullOrEmpty(level.title)) level.title = "未命名关卡";
            Directory.CreateDirectory(CustomFolder);
            File.WriteAllText(PathFor(level.id), level.ToXsb(true));

            var copy = level.Clone();
            int idx = Custom.FindIndex(l => l.id == level.id);
            if (idx >= 0) Custom[idx] = copy; else Custom.Add(copy);
            return copy;
        }

        public void DeleteCustom(string id)
        {
            try
            {
                var p = PathFor(id);
                if (File.Exists(p)) File.Delete(p);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sokoban] Delete failed: " + e.Message);
            }
            Custom.RemoveAll(l => l.id == id);
        }

        private string PathFor(string id)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
            return Path.Combine(CustomFolder, id + ".xsb");
        }
    }
}
