# Product

## Register

product

## Users

Internal factory / manufacturing operations staff and administrators who run and supervise a hardware test-and-deployment system (MProject). Primary personas:

- **Test-line operators**: work at or near test stations on a shop floor, often under bright ambient light, switching between physical hardware and the screen. They need to see state at a glance and act fast.
- **Engineers / line leads**: manage software releases, override files, config baselines, agent releases, and approvals; care about correctness and traceability.
- **Admins**: manage organization (teams/departments), users, products, computer fleet, and RBAC permissions.

The job to be done: keep test stations running the right software, deploy/update agents safely, approve changes, and monitor fleet health — with as little ambiguity and as few mistakes as possible.

## Product Purpose

MProject is the operations console that replaces the legacy Foxconn/Ubiquiti test/auto-download tooling. It exists to make manufacturing test operations observable and controllable from one place: software assignment and deployment, agent self-update releases, override files and config baselines, approvals with RBAC, organization management, and computer-fleet health.

Success looks like: an operator or engineer can read the current state of any station or release without guessing, take the correct action with confidence, and trust that destructive or outward-facing actions are gated and traceable.

## Brand Personality

Three words: **Trustworthy · Tidy · Professional**.

Voice and tone: calm, precise, operational. Plain labels over clever copy. The interface should feel like a dependable industrial instrument — information-dense where it must be, but never noisy. It earns trust by being legible and predictable, not by being flashy.

Emotional goal: confidence and control. The operator should never feel the UI is hiding state or guessing on their behalf.

## Anti-references

This must NOT look like any of the following (all explicitly rejected by the team):

- **Generic SaaS dashboard**: purple→blue gradients, identical card grids, the hero-metric template (big number + small label + gradient accent), tiny uppercase tracked eyebrows above every section.
- **Flashy marketing landing page**: decorative glassmorphism, heavy effects, bounce/elastic easing, motion as decoration.
- **Dense terminal / Bloomberg console**: crammed number grids, monospace everywhere, near-zero whitespace.
- **"AI-smell" UI**: Inter for everything, flat gray palette, gray text on colored backgrounds, colored left-stripe borders on cards/alerts.

## Design Principles

1. **State legibility first.** Every screen answers "what is true right now?" before it offers actions. Status, version, and ownership are never ambiguous.
2. **Calm density.** Pack the information operators need, but with rhythm and whitespace — dense is not the same as cramped. No noise for noise's sake.
3. **One disciplined accent.** A single brand accent (`#465fff`) carries emphasis and interactive affordance; color is meaning, not decoration.
4. **Predictable, gated actions.** Destructive or outward-facing actions are clearly marked, confirmed, and traceable. The UI never surprises the operator.
5. **Reuse over reinvention.** Honor the existing system (antd + Tailwind, shared i18n en/vi, established components and tokens). New dependencies and new patterns must earn their place.

## Accessibility & Inclusion

Target **WCAG 2.1 AA**.

- Body text ≥ 4.5:1 contrast; large text (≥18px or bold ≥14px) ≥ 3:1; placeholders meet the same 4.5:1 (no muted-gray-on-tint).
- Visible focus indicators and full keyboard navigation; logical tab order; no keyboard traps.
- Semantic HTML and proper ARIA roles/labels/states on interactive elements; correct heading hierarchy and landmarks.
- Form inputs always labeled, with clear required indicators and error messaging.
- Honor `prefers-reduced-motion` for every animation (crossfade or instant fallback).
- Bilingual (English / Vietnamese) via the existing i18n system — copy must stay translatable, no hard-coded user-facing strings.
- Shop-floor context: legible under bright ambient light and at a glance; comfortable touch targets (≥44×44px) where touch is used.
