TASK: B043
AGENT: orchestrator (direct fix at the user's request)
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs (line 249), docs/UI_SPEC.md (HUD mock-up and the warning row)
SUMMARY: The HUD warning label under the damage bar now reads "DAMAGE" instead of "SMOKE". Only the text changed: same position, size, ink colour, visibility rule (damage >= smokeThreshold) and flashing with the bar. Field and object names (_smokeText, "SmokeWarning") are unchanged, so no test needed changes (no test asserts the label text).
VERIFY: python tools/compile_check.py (exit 0). In Play: take damage past 70%; the flashing label under the bar says DAMAGE.
REQUESTS: none
