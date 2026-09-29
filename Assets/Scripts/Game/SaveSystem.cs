using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sokoban.Game
{
    [Serializable]
    public class LevelProgress
    {
        public string id;
        public bool completed;
        public int bestMoves;
        public int bestPushes;
        public float bestTime;
        public int stars;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public List<LevelProgress> progress = new List<LevelProgress>();
        public bool sfxEnabled = true;
        public float volume = 0.8f;
        public int animSpeed = 1; // 0 slow, 1 normal, 2 fast
        public bool unlockAll;
        public bool showDeadlockWarnings = true;
    }

    /// <summary>JSON save file in Application.persistentDataPath (atomic write).</summary>
    public class SaveSystem
    {
        public SaveData Data { get; private set; }
        private readonly string _path;
        private readonly Dictionary<string, LevelProgress> _index = new Dictionary<string, LevelProgress>();

        public SaveSystem()
        {
            _path = Path.Combine(Application.persistentDataPath, "save.json");
            Load();
        }

        public void Load()
        {
            Data = null;
            try
            {
                if (File.Exists(_path)) Data = JsonUtility.FromJson<SaveData>(File.ReadAllText(_path));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Sokoban] Save file corrupted, starting fresh: " + e.Message);
            }
            if (Data == null) Data = new SaveData();
            if (Data.progress == null) Data.progress = new List<LevelProgress>();
            _index.Clear();
            foreach (var p in Data.progress)
                if (p != null && !string.IsNullOrEmpty(p.id)) _index[p.id] = p;
        }

        public void Save()
        {
            try
            {
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sokoban] Failed to save: " + e.Message);
            }
        }

        public LevelProgress Get(string id)
        {
            LevelProgress p;
            return id != null && _index.TryGetValue(id, out p) ? p : null;
        }

        public bool IsCompleted(string id)
        {
            var p = Get(id);
            return p != null && p.completed;
        }

        /// <summary>Records a win; returns true when this is a new best (fewer moves).</summary>
        public bool RecordWin(string id, int moves, int pushes, float time, int stars)
        {
            var p = Get(id);
            bool newBest = false;
            if (p == null)
            {
                p = new LevelProgress { id = id };
                Data.progress.Add(p);
                _index[id] = p;
            }
            if (!p.completed || moves < p.bestMoves || (moves == p.bestMoves && pushes < p.bestPushes))
            {
                newBest = p.completed; // first completion is not a "new record"
                p.bestMoves = moves;
                p.bestPushes = pushes;
            }
            if (!p.completed || time < p.bestTime) p.bestTime = time;
            p.stars = Mathf.Max(p.stars, stars);
            p.completed = true;
            Save();
            return newBest;
        }

        public void ClearProgress(string id)
        {
            var p = Get(id);
            if (p == null) return;
            Data.progress.Remove(p);
            _index.Remove(id);
            Save();
        }

        public void ResetAllProgress()
        {
            Data.progress.Clear();
            _index.Clear();
            Save();
        }

        public static int ComputeStars(int moves, int par)
        {
            if (par <= 0) return 3;
            if (moves <= par) return 3;
            if (moves <= Mathf.CeilToInt(par * 1.5f) + 2) return 2;
            return 1;
        }
    }
}
