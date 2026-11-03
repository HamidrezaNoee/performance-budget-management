# ECC adoption — performance-budget-management

Original ECC MIT skill `architecture-decision-records`, vendored unchanged from https://github.com/affaan-m/ECC/tree/b38be6a7f6350964d59328b52f0624e1d1d552d1/skills/architecture-decision-records to `.agents/skills/architecture-decision-records/SKILL.md`.

## Repository-specific policy
Planned ASP.NET Core / React enterprise budgeting: record multi-company accounting, currency, fiscal-calendar, RTL/Jalali, and integration decisions as proposals until approved.

## Usage
1. Read `README.md`, `AGENTS.md` if present, relevant architecture, accepted ADRs, analysis status and outstanding decisions before drafting.
2. Invoke **$architecture-decision-records** for proposed design changes. Search existing ADR identifiers and naming before creating new ADRs; avoid duplicate numbers and don't rewrite accepted records without review.
3. Distinguish status **proposed**, **accepted**, **rejected**, **superseded**. Only mark accepted following an explicit decision by the owner or an authorized governance process.
4. For every significant decision, capture context, evidence, alternatives, trade-offs, impacts (data, compatibility, security, localization, cost), and rollback/migration requirements.
5. Use a draft PR for proposed ADRs. Project-specific instructions prevail over ECC sample folder layout; do not invent architecture as implemented code.

## Scope
No running installer, plugin, hook, GitHub workflow, MCP server, secrets, CI or production changes. Instructions only; existing project files remain unchanged. Upstream MIT terms apply.
