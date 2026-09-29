using NUnit.Framework;
using Sokoban.Core;

namespace Sokoban.Tests
{
    public class CoreTests
    {
        private const string Simple =
            "Title: Simple\n" +
            "Par: 2\n" +
            "#####\n" +
            "#@$.#\n" +
            "#####\n";

        [Test]
        public void Parser_ReadsMapAndMetadata()
        {
            var lvl = LevelParser.ParseSingle(Simple);
            Assert.IsNotNull(lvl);
            Assert.AreEqual("Simple", lvl.title);
            Assert.AreEqual(2, lvl.par);
            Assert.AreEqual(5, lvl.width);
            Assert.AreEqual(3, lvl.height);
            Assert.AreEqual(1, lvl.CountBoxes());
            Assert.AreEqual(1, lvl.CountGoals());
            Assert.AreEqual(1, lvl.CountPlayers());
        }

        [Test]
        public void Parser_ReadsMultipleLevelsAndPadsRows()
        {
            string text = "; collection\nTitle: A\n####\n#@.#\n#$ #\n####\n\nTitle: B\n #####\n##@$.#\n######\n";
            var list = LevelParser.ParseCollection(text);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("A", list[0].title);
            Assert.AreEqual("B", list[1].title);
            Assert.AreEqual(6, list[1].width);
            foreach (var row in list[1].rows) Assert.AreEqual(6, row.Length);
        }

        [Test]
        public void Serialize_RoundTrips()
        {
            var lvl = LevelParser.ParseSingle(Simple);
            lvl.author = "me";
            var back = LevelParser.ParseSingle(lvl.ToXsb(true));
            Assert.IsTrue(lvl.SameLayout(back));
            Assert.AreEqual("me", back.author);
            Assert.AreEqual(lvl.par, back.par);
        }

        [Test]
        public void Game_PushSolvesLevel()
        {
            var g = new SokobanGame(LevelParser.ParseSingle(Simple));
            Assert.IsFalse(g.IsSolved);
            Assert.AreEqual(MoveResult.Pushed, g.TryMove(Direction.Right));
            Assert.IsTrue(g.IsSolved);
            Assert.AreEqual(1, g.Moves);
            Assert.AreEqual(1, g.Pushes);
            Assert.AreEqual("R", g.GetHistoryLurd());
        }

        [Test]
        public void Game_BlockedByWallAndDoubleBox()
        {
            var g = new SokobanGame(LevelParser.ParseSingle("######\n#@$$.#\n######\n"));
            Assert.AreEqual(MoveResult.Blocked, g.TryMove(Direction.Right));
            Assert.AreEqual(MoveResult.Blocked, g.TryMove(Direction.Up));
            Assert.AreEqual(0, g.Moves);
        }

        [Test]
        public void Game_UndoRedoRestart()
        {
            var g = new SokobanGame(LevelParser.ParseSingle("#######\n#@ $ .#\n#######\n"));
            g.TryMove(Direction.Right);
            g.TryMove(Direction.Right);
            Assert.AreEqual(1, g.Pushes);
            Assert.IsTrue(g.Undo());
            Assert.AreEqual(0, g.Pushes);
            Assert.IsTrue(g.IsBox(new Int2(3, 1)));
            Assert.IsTrue(g.Redo());
            Assert.IsTrue(g.IsBox(new Int2(4, 1)));
            Assert.IsFalse(g.CanRedo);
            g.TryMove(Direction.Right);
            Assert.IsTrue(g.IsSolved);
            g.Restart();
            Assert.AreEqual(0, g.Moves);
            Assert.AreEqual(new Int2(1, 1), g.PlayerPos);
            Assert.IsTrue(g.IsBox(new Int2(3, 1)));
        }

        [Test]
        public void Game_NewMoveClearsRedo()
        {
            var g = new SokobanGame(LevelParser.ParseSingle("######\n#@  .#\n# $  #\n######\n"));
            g.TryMove(Direction.Right);
            g.Undo();
            Assert.IsTrue(g.CanRedo);
            g.TryMove(Direction.Down);
            Assert.IsFalse(g.CanRedo);
        }

        [Test]
        public void Game_WalkPathAvoidsBoxes()
        {
            var g = new SokobanGame(LevelParser.ParseSingle("#####\n#@  #\n#$# #\n#. .#\n#$  #\n#####\n"));
            var path = g.FindWalkPath(new Int2(1, 4));
            Assert.IsNull(path, "cell occupied by box");
            path = g.FindWalkPath(new Int2(2, 3));
            Assert.IsNotNull(path);
            Assert.AreEqual(5, path.Count);
        }

        [Test]
        public void Solver_SolvesAndSolutionReplays()
        {
            string lvlText =
                "  #####\n" +
                "###   #\n" +
                "#.@$  #\n" +
                "### $.#\n" +
                "#.##$ #\n" +
                "# # . ##\n" +
                "#$ *$$.#\n" +
                "#   .  #\n" +
                "########\n";
            var lvl = LevelParser.ParseSingle(lvlText);
            var res = Solver.Solve(lvl, new SolverOptions());
            Assert.AreEqual(SolveStatus.Solved, res.status, res.ToString());
            var g = new SokobanGame(lvl);
            Assert.AreEqual(res.moves, g.PlayMoves(res.solution));
            Assert.IsTrue(g.IsSolved);
        }

        [Test]
        public void Solver_DetectsUnsolvable()
        {
            // Box stuck in a corner that is not a goal
            var lvl = LevelParser.ParseSingle("#####\n#$  #\n# @.#\n#####\n");
            var res = Solver.Solve(lvl, new SolverOptions());
            Assert.AreEqual(SolveStatus.Unsolvable, res.status);
        }

        [Test]
        public void Validator_ReportsProblems()
        {
            var ok = LevelParser.ParseSingle(Simple);
            Assert.AreEqual(0, LevelValidator.Validate(ok).Count);

            var noPlayer = LevelParser.ParseSingle("#####\n# $.#\n#####\n");
            Assert.Greater(LevelValidator.Validate(noPlayer).Count, 0);

            var mismatch = LevelParser.ParseSingle("######\n#@$$.#\n######\n");
            Assert.Greater(LevelValidator.Validate(mismatch).Count, 0);

            var open = new LevelData(5, 3);
            open.Set(1, 1, '@'); open.Set(2, 1, '$'); open.Set(3, 1, '.');
            Assert.Greater(LevelValidator.Validate(open).Count, 0);
        }

        [Test]
        public void LevelData_ResizeTrimShift()
        {
            var lvl = LevelParser.ParseSingle(Simple);
            lvl.Resize(8, 6);
            Assert.AreEqual(8, lvl.width);
            Assert.AreEqual(6, lvl.height);
            lvl.Shift(1, 1);
            Assert.AreEqual('@', lvl.Get(2, 2));
            lvl.Trim();
            Assert.AreEqual(5, lvl.width);
            Assert.AreEqual(3, lvl.height);
            Assert.AreEqual('@', lvl.Get(1, 1));
        }

        [Test]
        public void Game_SnapshotSolvableForHints()
        {
            var lvl = LevelParser.ParseSingle("#######\n#@ $ .#\n#######\n");
            var g = new SokobanGame(lvl);
            g.TryMove(Direction.Right);
            var snap = g.ToLevelData();
            var res = Solver.Solve(snap, new SolverOptions());
            Assert.AreEqual(SolveStatus.Solved, res.status);
            Assert.AreEqual("RR", res.solution);
        }
    }
}
