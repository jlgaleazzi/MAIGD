TASK: T037
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs
SUMMARY: Created ResultView per UI_SPEC §3.5 with two distinct layouts (Won/Lost). On win it calls SaveSystem.SaveBestScore and unlocks next mission. "Next" button hidden on mission 5; Retry delegates to GameManager.RetryMission; Next delegates to GameManager.StartMission(m+1) or ShowMainMenu. Shows best-score comparison when a record exists.
VERIFY: static check passes — Unity not available (UNITY_PATH unset). Code references: SaveSystem.GetBestScore, SaveSystem.SaveBestScore (both confirmed in SaveSystem.cs), GameManager.Instance.RetryMission/start/ShowMainMenu (to be implemented by the GameManager task). Build with `tools/unity.sh compile` when UNITY_PATH is set. In-editor: assign ResultView component to a GameObject on the results canvas; call `resultView.Show(missionIndex, won, score)` from GameManager on MissionEnded.
REQUESTS: GameManager class needs public methods `RetryMission(int)`, `StartMission(int)`, and `ShowMainMenu()` for button delegates to work. If these names change, ResultView must be updated accordingly.
