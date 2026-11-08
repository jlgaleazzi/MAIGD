TASK: T007
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Core/TeamTag.cs
SUMMARY: Created TeamTag.cs containing the Team enum and TeamTag component.
  The TeamTag class derives from MonoBehaviour with a [SerializeField] private team
  field and a read-only public property (per ARCHITECTURE §7). Default = Neutral.
VERIFY: When Unity is available, run tools/unity.sh compile (expect exit 0).
  In-editor: attach TeamTag to any GameObject in the Scene view inspector;
  confirm the "Team" dropdown shows Player / Enemy / Neutral with default Neutral.
REQUESTS: orchestrator — ensure Biplanes/Assets/Biplanes/Scripts/Core/ directory exists
  before next agents write here. Next dependency: IDamageable interface (for Health.cs).
