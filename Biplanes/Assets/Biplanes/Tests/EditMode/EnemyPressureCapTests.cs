using NUnit.Framework;
using UnityEngine;
using Biplanes.Missions;

namespace Biplanes.Tests {

    /// <summary>
    /// B041 (QA T958 N1): the maxLiveFighters cap counts only fighters on screen or closing on
    /// the view in the camera's frame. Fighters off screen that hold their distance or recede
    /// (they can never be seen again before they escape) don't block a reinforcement wave.
    /// relativeVelocityX = fighter world x speed - camera x speed, as in the B040 escape check.
    /// </summary>
    public class EnemyPressureCapTests {

        // 16:9 view, 10 units tall, centred on x = 100.
        private static readonly Rect View = new Rect(100f - 8.8889f, -5f, 17.7778f, 10f);
        private static float Range => View.width * EnemyPressure.CapRangeScreens;

        private static bool Counts(float x, float fighterVelX, float cameraVelX) {
            return EnemyPressure.CountsTowardCap(x, fighterVelX - cameraVelX, View, Range);
        }

        [Test]
        public void OnScreen_AlwaysCounts_WhateverTheVelocity() {
            foreach (float x in new[] { View.xMin, View.center.x, View.xMax }) {
                foreach (float rel in new[] { -6f, -3f, 0f, 3f, 6f }) {
                    Assert.IsTrue(EnemyPressure.CountsTowardCap(x, rel, View, Range), $"x {x} rel {rel}");
                }
            }
        }

        [Test]
        public void HitboxOverlappingTheEdge_Counts_EvenWhenReceding() {
            float half = EnemyPressure.FighterHalfWidth;
            Assert.IsTrue(EnemyPressure.CountsTowardCap(View.xMin - half + 0.01f, -5f, View, Range));
            Assert.IsTrue(EnemyPressure.CountsTowardCap(View.xMax + half - 0.01f, 5f, View, Range));
        }

        [Test]
        public void OffScreen_Closing_Counts() {
            // Right of the view flying left, left of the view flying right.
            Assert.IsTrue(EnemyPressure.CountsTowardCap(View.xMax + 5f, -3f, View, Range));
            Assert.IsTrue(EnemyPressure.CountsTowardCap(View.xMin - 5f, 3f, View, Range));
        }

        [Test]
        public void OffScreen_Receding_DoesNotCount() {
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMin - 5f, -1f, View, Range));
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMax + 5f, 1f, View, Range));
        }

        [Test]
        public void OffScreen_HoldingDistance_DoesNotCount() {
            // N1 (a): M3/M4 scroll 3 = fighter speed 3, the fighter sits off the edge forever.
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMin - 5f, 0f, View, Range));
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMax + 5f, 0f, View, Range));
            // Within the epsilon also counts as holding distance.
            float e = EnemyPressure.ClosingSpeedEpsilon * 0.5f;
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMin - 5f, e, View, Range));
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMax + 5f, -e, View, Range));
        }

        [Test]
        public void BeyondTheCapRange_NeverCounts_EvenClosing() {
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMax + Range + 1f, -6f, View, Range));
            Assert.IsFalse(EnemyPressure.CountsTowardCap(View.xMin - Range - 1f, 6f, View, Range));
        }

        [Test]
        public void CameraMoving_M3_PlayerChasesAPassedFighterLeft_DoesNotCount() {
            // Camera -3 (facing left), fighter -3 already left of the view: relative 0.
            Assert.IsFalse(Counts(View.xMin - 4f, -3f, -3f));
            // Camera +3 (facing right), JSON fighter ahead flying left: closing at 6.
            Assert.IsTrue(Counts(View.xMax + 4f, -3f, 3f));
        }

        [Test]
        public void CameraMoving_M1_FighterDriftingBehind_DoesNotCount_FasterCameraCatchesUp_Counts() {
            // M1: camera -2, fighter -3 left of the view: it gains 1 u/s on the camera. Receding.
            Assert.IsFalse(Counts(View.xMin - 4f, -3f, -2f));
            // Camera +2 (player turned back right), fighter right of it flying right at 3: receding.
            Assert.IsFalse(Counts(View.xMax + 4f, 3f, 2f));
            // Camera -3 vs a fighter flying left at -2 on the left: the view catches up. Counts.
            Assert.IsTrue(Counts(View.xMin - 4f, -2f, -3f));
        }

        [Test]
        public void CameraPinned_UsesTheFighterSpeedAlone() {
            // Camera pinned at a mission limit (velocity 0).
            Assert.IsTrue(Counts(View.xMax + 4f, -3f, 0f));
            Assert.IsFalse(Counts(View.xMin - 4f, -3f, 0f));
            // Static (or escorting) fighter off screen with a pinned camera never arrives.
            Assert.IsFalse(Counts(View.xMax + 4f, 0f, 0f));
        }

        [Test]
        public void RecederNoLongerBlocksAWave() {
            // Cap 6, wave 2: four receding ghosts used to block a wave of 2 (N1).
            int counted = 0;
            for (int i = 0; i < 4; i++) {
                if (EnemyPressure.CountsTowardCap(View.xMin - 2f - i, 0f, View, Range)) counted++;
            }
            Assert.AreEqual(0, counted);
            Assert.AreEqual(2, EnemyPressure.WaveSize(2, 6, counted));
        }
    }
}
