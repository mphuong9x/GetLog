# M-System Design System v1

## 1. Design Direction

M-System is an internal engineering and manufacturing management application.

The UI should feel:

* Professional
* Technical
* Industrial
* Information-dense
* Calm
* Consistent
* Optimized for long daily usage
* Desktop-first

The visual identity should be based primarily on the existing **User Management page**.

Use a:

**Carbon / Charcoal Dark foundation + Indigo / Electric Blue primary accent**

Do not redesign M-System into a generic SaaS dashboard.

Avoid:

* Glassmorphism
* Large gradients
* Neon effects
* Oversized cards
* Excessive shadows
* Excessive rounded corners
* Decorative charts/statistics
* Marketing-style layouts
* Large empty areas
* Different color identities between pages

---

# 2. Core Color System

The following values define the target palette.

Exact values may be adjusted slightly to match the existing application's CSS, but the visual relationships should remain consistent.

## Carbon / Charcoal Neutrals

### App Canvas

`#0D1117`

Use for:

* Main application background
* Large empty workspace areas

This should not be pure black.

---

### Sidebar / Deep Surface

`#0B0F14`

Use for:

* Main sidebar
* Deep navigation surfaces

It should be slightly darker than the main application canvas.

---

### Primary Surface

`#15191F`

Use for:

* Tables
* Main panels
* Forms
* Department/team content areas
* Standard containers

---

### Elevated Surface

`#1B2028`

Use for:

* Inputs
* Dropdowns
* Hovered rows
* Expanded sections
* Context menus
* Elevated controls

Do not create excessive elevation.

---

### Interactive / Hover Surface

`#202630`

Use sparingly for:

* Hover states
* Selected secondary elements
* Interactive rows

---

# 3. Borders and Dividers

## Default Border

`#252B34`

Use for:

* Table separators
* Input borders
* Panel boundaries
* Section separators

## Strong Border

`#303846`

Use only where stronger structural separation is necessary.

## Rule

Prefer:

**surface contrast → subtle divider → border**

in that order.

Do not put visible borders around every element.

Avoid the "boxes inside boxes" appearance.

---

# 4. Typography Colors

## Primary Text

`#F1F5F9`

Use for:

* Page titles
* Names
* Important values
* Primary labels

---

## Secondary Text

`#94A3B8`

Use for:

* Descriptions
* Metadata
* Secondary labels
* Supporting information

---

## Muted Text

`#64748B`

Use for:

* Placeholders
* Disabled information
* Minor metadata
* Tertiary labels

Never use very low-contrast gray for important information.

---

# 5. Primary Brand / Interaction Color

M-System should use **Indigo / Electric Blue** rather than cyan as its primary interaction color.

## Primary Blue

`#5268FF`

Use for:

* Primary buttons
* Important active controls
* Main call-to-action
* Selected navigation accent where appropriate

## Hover

`#6478FF`

## Active / Pressed

`#4055E8`

## Subtle Blue Background

`#121936`

Use for:

* Selected sidebar item
* Selected department
* Active navigation
* Selected table/filter state

## Blue Border

`#304066`

Use for:

* Blue badges
* Focused controls
* Selected-state boundaries

---

# 6. Accent Usage Rule

The interface should remain predominantly neutral.

Approximate visual balance:

**85–90% Carbon / Charcoal neutrals**

**5–10% Blue / Indigo interaction color**

**<5% semantic colors**

Blue is functional, not decorative.

Use blue to tell the user:

* This is selected
* This is clickable
* This is active
* This is the primary action
* This currently has focus

Do NOT:

* Draw blue borders around every card
* Make every icon blue
* Make all text links bright blue
* Use blue purely as decoration

---

# 7. Semantic Colors

Semantic colors communicate state, not branding.

## Success / Active

Target:

`#00D89C`

Use for:

* Active
* Successful
* Healthy
* Completed states

Example:

`● Active`

Prefer green text/icon with minimal background treatment.

---

## Warning / Pending

Target:

`#FFB800`

Use for:

* Pending
* Warning
* Requires attention

Example:

`● Pending`

---

## Danger / Destructive

Target:

`#EF4444`

Use for:

* Delete
* Errors
* Failed states
* Destructive confirmation

Do NOT make Delete permanently visually dominant.

Default delete icons/buttons should be restrained.

Use stronger red on:

* Hover
* Focus
* Confirmation
* Actual error state

---

## Inactive / Disabled

Use approximately:

`#64748B`

for inactive or disabled states.

---

# 8. Typography Scale

Optimize primarily for desktop screens between approximately 1366px and 1920px.

## Page Title

20–22px
Semibold

## Major Section Title

14–16px
Semibold

## Section Label

12–13px
Medium / Semibold

Uppercase may be used sparingly for structural labels such as:

`DEPARTMENTS`

`TEAMS`

`LEADERS`

`MEMBERS`

## Standard Body

13–14px
Regular

## Secondary / Metadata

12–13px
Regular

## Table Header

11–12px
Medium

## Caption

11–12px

Avoid oversized typography.

M-System should prioritize information visibility over visual drama.

---

# 9. Spacing System

Use a consistent spacing scale:

`4 / 8 / 12 / 16 / 20 / 24px`

Preferred usage:

* Icon ↔ text: 8px
* Related controls: 8px
* Internal row padding: 12–16px
* Related sections: 16px
* Major sections: 20–24px

Avoid arbitrary spacing values unless necessary.

---

# 10. Density

M-System is an operational system.

It should be information-dense without becoming cramped.

Target:

### Standard management row

Approximately:

`44–50px`

### Compact secondary row

Approximately:

`36–42px`

At 1920×1080, users should see a meaningful amount of operational data without excessive scrolling.

Avoid large empty areas purely for aesthetic purposes.

---

# 11. Border Radius

Use restrained rounding.

Suggested:

Small controls:

`4–6px`

Inputs / Buttons:

`6px`

Panels:

`6–8px`

Large cards should generally not exceed:

`8px`

Avoid the highly rounded SaaS appearance.

---

# 12. Buttons

## Primary Button

Use for the most important action on the current page.

Example:

`Apply Filters`

Style:

* Indigo blue background
* High-contrast text
* Compact height
* Moderate radius
* Clear hover state

Do not have several primary buttons competing on one screen.

---

## Secondary Button

Use dark surface + subtle border.

Examples:

`Reset`

`Edit`

---

## Section Action

Prefer compact text action:

`+ Add team`

`+ Add member`

`+ Add leader`

Use blue accent but avoid large button containers unless the action is particularly important.

---

## Destructive Action

Prefer icon or subtle secondary button.

Red should become visually prominent mainly on hover/confirmation.

---

# 13. Inputs and Filters

Follow the User Management visual language.

Inputs should use:

* Dark elevated surface
* Subtle border
* Muted placeholder
* Clear blue focus state
* Compact height

Labels should remain visible above inputs when needed.

Do not rely exclusively on placeholders for field meaning.

---

# 14. Tables

Tables are a core M-System UI pattern.

Use:

* Minimal vertical separators
* Subtle horizontal dividers
* Compact rows
* Clear column alignment
* Muted uppercase/small headers
* Strong primary value
* Secondary information below primary value when useful

Example:

NAME

`Phuong Minh`
`No email`

rather than allocating unnecessary columns for every secondary property.

Row hover should use a subtle elevated Carbon surface.

Avoid zebra-striping unless the table becomes difficult to scan.

---

# 15. Badges

Badges should be compact.

Use badges for:

* Roles
* Team assignments
* Permission indicators
* Small categorical states

Examples:

`Global Viewer`

`TeamLeader`

`AP-CFT`

Use:

* Dark/subtle background
* Thin border
* Blue or semantic text where meaningful

Do not make every badge brightly filled.

When many badges exist in one row, they should remain visually subordinate to the person's name.

---

# 16. Navigation

Follow the existing User Management sidebar.

Selected navigation:

* Subtle Indigo/Blue background
* Blue icon/text
* Clear but restrained active state

Unselected navigation:

* Neutral text
* Muted icons

Hover:

* Slightly lighter Carbon surface

Do not use large glowing indicators or decorative effects.

---

# 17. Organization Page Application

The Organization page should use this same design system.

Preserve:

`Department → Team → Leaders / Members`

## Department Rail

Use:

* Primary Surface
* Compact department rows
* Subtle dividers
* Blue-subtle selected background
* Primary department name
* Muted `teams · users` metadata

Selected department should be obvious without requiring a bright cyan border.

---

## Department Header

Department name is primary.

Example:

`AP`

Secondary:

`Production Group A`

Right-side metadata:

`2 Teams`
`3 Users`

Actions:

`View production details`
`Edit`
`Delete`

Keep these compact.

---

## Teams

Team header should be a compact expandable operational row.

Example:

`⌄  CFT        3 members                         ✎  ⋯`

Expanded content should use a slightly differentiated Carbon surface rather than a bright border.

---

## Leaders / Members

Use proper column headers.

Leaders:

`NAME / USERNAME | ASSIGNED DATE | SHIFT | ACTIONS`

Members:

`NAME / USERNAME | ROLE | SHIFT | ASSIGNED DATE | ACTIONS`

Leader status may use a restrained role badge where appropriate.

---

# 18. Interaction States

Every interactive component should define:

* Default
* Hover
* Active
* Focus
* Disabled

Focus should use the Primary Blue family and remain clearly visible for keyboard users.

Do not remove focus outlines without providing an accessible replacement.

---

# 19. Consistency Rule

When redesigning an existing M-System page:

**Do not independently invent a new visual language for that page.**

Reuse this system for:

* Backgrounds
* Surfaces
* Borders
* Typography
* Buttons
* Inputs
* Tables
* Badges
* Navigation
* Selected states
* Semantic statuses

Individual pages may have different layouts, but they should clearly belong to the same application.

---

# 20. Reference Priority

When there is uncertainty about visual styling, use this priority:

1. Existing User Management page
2. M-System Design System v1
3. Existing application components
4. New design decisions only when necessary

The User Management screenshot is the primary visual reference.

Do not replace its established visual identity with a generic AI-generated admin/SaaS aesthetic.

---

# Final Design Principle

M-System should feel like:

**A mature engineering and manufacturing operations application built for daily professional use.**

Not:

**A SaaS dashboard template.**

The design should prioritize:

**clarity → hierarchy → information density → consistency → aesthetics.**

Aesthetics should improve usability, not compete with it.
