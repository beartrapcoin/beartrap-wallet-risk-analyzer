You are helping me build a hackathon MVP called BearTrap.

Project goal:
Build a lightweight ASP.NET Core 8 Razor Pages web app that analyzes the latest 5 tokens launched on Four.Meme (BNB Chain) and calculates a simple risk score.

Architecture constraints:
- ASP.NET Core 8
- Razor Pages (no MVC controllers)
- Entity Framework Core
- SQLite database
- Clean layered structure (Services, Data, Domain)
- Minimal dependencies
- Clean, readable, modular code
- No overengineering

Core features:
1. Fetch 5 latest tokens from Four.Meme via external API.
2. Store tokens locally in SQLite.
3. Analyze token using rule-based RiskAnalyzer.
4. Calculate Risk Score (0-100).
5. Store RiskReport.
6. Generate AI explanation (OpenClaw integration).
7. Display results in Razor Pages UI.

Important:
- Risk scoring must be deterministic and rule-based.
- AI must not influence score calculation.
- Code must be clean and extensible.
- Follow SOLID where reasonable for MVP.
- Prefer simplicity over abstraction.
