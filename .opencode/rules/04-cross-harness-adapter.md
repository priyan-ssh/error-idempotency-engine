
# Rule: Cross-Harness Adapter
- **Never edit** generated files under `.agents/` directly. They are overwritten by the sync script.
- All changes go into `core/agents/*.md`, `core/skills/*/SKILL.md`, or `core/rules/*.md`.
- Run `python3 scripts/sync-harnesses.py` after every core edit to propagate changes to `.agents/`.
- The sync script derives its paths from its own location — it works on any machine without modification.
