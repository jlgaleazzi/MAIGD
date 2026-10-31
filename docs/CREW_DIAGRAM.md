# Biplanes crew: roles, connections and data flow

## 1. Roles and data flow

```mermaid
flowchart TB
    %% ---------- Human ----------
    subgraph H["Human (Juan)"]
        CMD["/kickoff · /produce · /mission N<br/>/qa-pass M# · /release X.Y.Z"]
        PLAY["Manual playtest<br/>PC · Mac · WebGL"]
    end

    %% ---------- Crew ----------
    subgraph CREW["opencode crew · Qwen3.6 35B on DGX Spark"]
        ORCH{{"Orchestrator (primary)<br/>plan · delegate · integrate"}}
        ART["Art Agent<br/>blueprint wireframes + pencil BGs"]
        MECH["Game Mechanics Agent<br/>C# framework, physics, AI, UI, builds"]
        MIS["Mission Design Agent<br/>5 missions as data"]
        QA["QA Agent<br/>tests, checks, bug reports"]
    end

    %% ---------- Shared contracts ----------
    subgraph DOCS["Shared contracts (read by all)"]
        GDD[("docs/GDD.md")]
        ARCH[("docs/ARCHITECTURE.md")]
        STYLE[("docs/ART_STYLE.md")]
        UISPEC[("docs/UI_SPEC.md")]
    end

    subgraph BOARD["Coordination"]
        TASKS[("docs/crew/TASKS.md")]
        HAND[("docs/crew/handoffs/T-nnn-agent.md")]
        QAREP[("docs/qa/ reports + bugs")]
    end

    %% ---------- Tools ----------
    subgraph TOOLS["tools/"]
        RAST["rasterize.py<br/>SVG to PNG + palette check"]
        VAL["validate_missions.py<br/>schema + pacing check"]
        UNI["unity.sh<br/>compile · test · scenes · builds"]
    end

    %% ---------- Unity project ----------
    subgraph UNITY["Unity 6 project: Biplanes/Assets/Biplanes"]
        SVG[("art-src/*.svg")]
        SPR[("Resources/Sprites/*.png")]
        CS[("Scripts/ + Editor/ProjectBuilder.cs")]
        BAL[("Resources/Balance.json")]
        MJ[("Resources/Missions/mission_01..05.json")]
        TST[("Tests/EditMode/")]
    end

    BUILDS[["Builds: Windows · macOS · WebGL"]]

    %% Human <-> orchestrator
    CMD -->|commands| ORCH
    ORCH -->|"progress, scope questions"| CMD
    PLAY -->|feel / feedback| ORCH
    QAREP -->|playtest checklist| PLAY

    %% Orchestrator owns contracts and board
    ORCH -->|owns / edits| GDD
    ORCH -->|owns / edits| ARCH
    ORCH -->|writes| UISPEC
    ORCH <-->|plan + status| TASKS

    %% Delegation (Task tool briefs)
    ORCH ==>|"task brief"| ART
    ORCH ==>|"task brief"| MECH
    ORCH ==>|"task brief"| MIS
    ORCH ==>|"verify brief"| QA

    %% Handoffs back
    ART -.->|handoff| HAND
    MECH -.->|handoff| HAND
    MIS -.->|handoff| HAND
    QA -.->|handoff| HAND
    HAND -.->|"integrate, REQUESTS become tasks"| ORCH
    QA -->|"PASS/FAIL + bugs P0-P2"| QAREP
    QAREP -->|bugs routed to owners| ORCH

    %% Art pipeline
    STYLE --> ART
    ART -->|writes| SVG
    SVG --> RAST --> SPR

    %% Mechanics pipeline
    ARCH --> MECH
    UISPEC --> MECH
    MECH -->|writes| CS
    MECH -->|writes| BAL
    MECH -->|runs| UNI

    %% Mission pipeline
    GDD --> MIS
    ARCH --> MIS
    MIS -->|writes| MJ
    MIS -->|runs| VAL
    MIS -.->|balance REQUESTS| HAND

    %% Runtime consumption
    SPR -->|"Resources.Load"| CS
    BAL -->|GameBalance| CS
    MJ -->|MissionRunner| CS

    %% QA pipeline
    QA -->|writes| TST
    QA -->|runs| UNI
    QA -->|runs| VAL
    QA -->|"runs --check"| RAST
    TST --> UNI
    CS --> UNI
    UNI -->|"ProjectBuilder.Build*"| BUILDS
    BUILDS --> PLAY

    classDef lead fill:#F2C14E,stroke:#0F2A4A,color:#0F2A4A
    classDef agent fill:#0F2A4A,stroke:#E8F1FA,color:#E8F1FA
    classDef qa fill:#E4572E,stroke:#0F2A4A,color:#ffffff
    classDef out fill:#7BD389,stroke:#0F2A4A,color:#0F2A4A
    class ORCH lead
    class ART,MECH,MIS agent
    class QA qa
    class BUILDS out
```

**Legend:** thick arrows `==>` are delegations (Task tool briefs). Dotted arrows `-.->`
are handoffs back to the orchestrator. Solid arrows are reads, writes and tool runs.

## 2. One task, end to end

```mermaid
sequenceDiagram
    autonumber
    actor Human
    participant O as Orchestrator
    participant S as Specialist (Art / Mechanics / Mission)
    participant T as tools/
    participant Q as QA
    participant B as TASKS.md + handoffs

    Human->>O: /produce
    O->>B: read board + new handoffs
    O->>O: pick next TODO (deps DONE, current milestone)
    O->>S: TASK brief (GOAL, READ, WRITE ≤3 files, ACCEPTANCE)
    S->>S: read ARCHITECTURE / STYLE / GDD sections
    S->>T: rasterize.py / validate_missions.py / unity.sh compile
    T-->>S: OK or errors (fix, up to 3 tries)
    S->>B: handoff T-nnn-agent.md (STATUS, FILES, VERIFY, REQUESTS)
    S-->>O: handoff
    O->>Q: verify brief for T-nnn
    Q->>T: compile + EditMode tests + data checks (static fallback if no Unity)
    Q->>B: docs/qa/T-nnn-verify.md (PASS/FAIL + bugs)
    Q-->>O: verdict
    alt PASS
        O->>B: mark DONE, turn REQUESTS into tasks
    else FAIL
        O->>B: create bug tasks (P0-P2) for owners
    end
    O-->>Human: milestone progress / scope questions
```

## 3. Milestones (six weeks)

```mermaid
flowchart LR
    M0["W1 · M0 Foundation<br/>core, player, scroll"] --> M1["W2 · M1 Mission 1 slice<br/>takeoff, fighters, HUD"]
    M1 --> M2["W3 · M2 Missions 2-3<br/>bomber, AAA, bombs"]
    M2 --> M3["W4 · M3 Missions 4-5<br/>balloons, base, zeppelin"]
    M3 --> M4["W5 · M4 Content complete<br/>all art, FX, save, balance"]
    M4 --> M5["W6 · M5 Release<br/>QA sign-off, 3 builds"]
```
