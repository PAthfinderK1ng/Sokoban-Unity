using System.IO;
using System.Text;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.EditorTools
{
    /// <summary>Unity editor menu utilities for content validation and debugging.</summary>
    public static class SokobanEditorTools
    {
        private const string BuiltInPath = "Assets/Resources/Levels/BuiltIn.txt";

        [MenuItem("Sokoban/验证内置关卡 (Validate Built-in Levels)")]
        public static void ValidateBuiltIn()
        {
            var text = File.ReadAllText(BuiltInPath);
            var levels = LevelParser.ParseCollection(text);
            var sb = new StringBuilder();
            int bad = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                var l = levels[i];
                EditorUtility.DisplayProgressBar("Sokoban", "Solving " + (i + 1) + "/" + levels.Count + " " + l.title, i / (float)levels.Count);
                var issues = LevelValidator.Validate(l);
                var res = Solver.Solve(l, new SolverOptions { maxNodes = 2000000 });
                bool ok = issues.Count == 0 && res.status == SolveStatus.Solved;
                if (!ok) bad++;
                sb.AppendLine((ok ? "OK   " : "FAIL ") + (i + 1).ToString("00") + " " + l.title + "  " + res +
                              (l.par > 0 && res.status == SolveStatus.Solved && res.moves != l.par ? "  (par in file: " + l.par + ")" : "") +
                              (issues.Count > 0 ? "  issues: " + string.Join("; ", issues.ToArray()) : ""));
            }
            EditorUtility.ClearProgressBar();
            if (bad == 0) Debug.Log("[Sokoban] All " + levels.Count + " built-in levels are valid and solvable.\n" + sb);
            else Debug.LogError("[Sokoban] " + bad + " built-in level(s) failed validation.\n" + sb);
        }

        [MenuItem("Sokoban/重新计算参考步数 (Recompute Par)")]
        public static void RecomputePar()
        {
            var levels = LevelParser.ParseCollection(File.ReadAllText(BuiltInPath));
            for (int i = 0; i < levels.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Sokoban", "Solving " + levels[i].title, i / (float)levels.Count);
                var res = Solver.Solve(levels[i], new SolverOptions { maxNodes = 2000000 });
                if (res.status == SolveStatus.Solved) levels[i].par = res.moves;
                if (string.IsNullOrEmpty(levels[i].id)) levels[i].id = "builtin-" + (i + 1).ToString("00");
            }
            EditorUtility.ClearProgressBar();
            File.WriteAllText(BuiltInPath, "; Sokoban built-in levels. Par = solver reference solution length (moves).\n\n" + LevelParser.SerializeCollection(levels));
            AssetDatabase.ImportAsset(BuiltInPath);
            Debug.Log("[Sokoban] Par values recomputed for " + levels.Count + " levels.");
        }

        [MenuItem("Sokoban/打开存档目录 (Open Save Folder)")]
        public static void OpenSaveFolder()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

        [MenuItem("Sokoban/清除存档 (Delete Save Data)")]
        public static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("Sokoban", "删除存档 save.json（不会删除自定义关卡）？", "删除", "取消")) return;
            var p = Path.Combine(Application.persistentDataPath, "save.json");
            if (File.Exists(p)) File.Delete(p);
            Debug.Log("[Sokoban] Save deleted: " + p);
        }
    }
}
