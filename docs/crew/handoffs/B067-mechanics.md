TASK: B067
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs (UpdateCrashFall)
SUMMARY: Fixes QA T969 BUG-1, where the wreck's nose snapped when the plane crashed in the first half of a turn. When VisualFacing flips during the crash fall, `_crashAngle` is now negated. The nose keeps its up/down attitude, the way BodyAngle mirrors in flight. After the flip the spin follows the new VisualFacing, so the nose keeps going down.
The fix also covers a wreck that has already landed: it re-applies MoveRotation with the mirrored angle. The sideways drift still uses `_facing`, the gameplay direction, unchanged.
VERIFY: `python tools/compile_check.py` -> PASS Biplanes, PASS Biplanes.Editor, exit 0. Unity batch was not run, per the brief. QA should rerun the EditMode suite (expect 602/602), including PlayerFlipQaTests.Crash_InTheFirstHalfOfATurn_WreckNoseDoesNotPopAtTheFlip (not edited) and PlayerFlipTests.Crash_MidTurn_FinishesTheTurnOnTheWreck.
REQUESTS: orchestrator: in the ARCHITECTURE.md B066 text, change "A crash mid-turn finishes the squash on the wreck" to "A crash mid-turn finishes the squash on the wreck; at the flip the wreck angle is mirrored (negated) so the nose keeps its up/down attitude (B067)".
