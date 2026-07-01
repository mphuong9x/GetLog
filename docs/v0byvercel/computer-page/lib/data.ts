export type ComputerStatus =
  | "online"
  | "offline"
  | "error"
  | "updating"
  | "registered"
  | "unknown"

export type Issue = "offline" | "error" | "disk" | "drift"

export interface Computer {
  id: string
  name: string
  station: string
  bay: string
  status: ComputerStatus
  model: string
  ip: string
  mac: string
  lastSeen: string
  cpu: number | null
  ram: number | null
  disk: number | null
  issues: Issue[]
}

export const STATUS_META: Record<
  ComputerStatus,
  { label: string; token: string }
> = {
  online: { label: "Online", token: "success" },
  offline: { label: "Offline", token: "neutral" },
  error: { label: "Error", token: "danger" },
  updating: { label: "Updating", token: "info" },
  registered: { label: "Registered", token: "info" },
  unknown: { label: "Unknown", token: "warning" },
}

export const ISSUE_META: Record<Issue, { label: string; token: string }> = {
  offline: { label: "Offline", token: "neutral" },
  error: { label: "Error / crash-loop", token: "danger" },
  disk: { label: "Disk ≥90%", token: "warning" },
  drift: { label: "Drift", token: "info" },
}

export const computers: Computer[] = [
  {
    id: "pc-qa-01",
    name: "PC-QA-01",
    station: "UTPG3TM0T01",
    bay: "DOWNLOAD",
    status: "error",
    model: "Dell OptiPlex 7010",
    ip: "10.177.23.101",
    mac: "2C-33-58-D9-93-A1",
    lastSeen: "07/01/2026, 10:24 AM",
    cpu: 95,
    ram: 91,
    disk: 97,
    issues: ["error", "disk"],
  },
  {
    id: "pc-spare-01",
    name: "PC-SPARE-01",
    station: "UTPG3T00T01",
    bay: "FT1",
    status: "error",
    model: "HP EliteDesk 800",
    ip: "10.177.23.102",
    mac: "2C-33-58-D9-93-A2",
    lastSeen: "07/01/2026, 10:22 AM",
    cpu: 88,
    ram: 78,
    disk: 92,
    issues: ["error", "disk"],
  },
  {
    id: "pc-dev-01",
    name: "PC-DEV-01",
    station: "UTPG3T00T01",
    bay: "FT2",
    status: "offline",
    model: "Dell OptiPlex 7010",
    ip: "10.177.23.103",
    mac: "2C-33-58-D9-93-A3",
    lastSeen: "07/01/2026, 08:11 AM",
    cpu: 5,
    ram: 20,
    disk: 45,
    issues: ["offline"],
  },
  {
    id: "pc-dev-02",
    name: "PC-DEV-02",
    station: "UTPG3TM0T01",
    bay: "PT0",
    status: "unknown",
    model: "Lenovo ThinkCentre",
    ip: "10.177.23.104",
    mac: "2C-33-58-D9-93-A4",
    lastSeen: "—",
    cpu: null,
    ram: null,
    disk: null,
    issues: [],
  },
  {
    id: "pc-line-a-01",
    name: "PC-LINE-A-01",
    station: "UTPG3TM0T01",
    bay: "FT1",
    status: "offline",
    model: "Dell OptiPlex 7010",
    ip: "10.177.23.105",
    mac: "2C-33-58-D9-93-A5",
    lastSeen: "07/01/2026, 06:40 AM",
    cpu: 25,
    ram: 40,
    disk: 55,
    issues: ["offline"],
  },
  {
    id: "pc-line-a-02",
    name: "PC-LINE-A-02",
    station: "UTPG3TM0T01",
    bay: "FT2",
    status: "offline",
    model: "HP EliteDesk 800",
    ip: "10.177.23.106",
    mac: "2C-33-58-D9-93-A6",
    lastSeen: "07/01/2026, 06:38 AM",
    cpu: 12,
    ram: 33,
    disk: 60,
    issues: ["offline"],
  },
  {
    id: "pc-line-b-01",
    name: "PC-LINE-B-01",
    station: "UTPG3TM0T01",
    bay: "FT3",
    status: "offline",
    model: "Dell OptiPlex 7010",
    ip: "10.177.23.107",
    mac: "2C-33-58-D9-93-A7",
    lastSeen: "07/01/2026, 05:59 AM",
    cpu: 75,
    ram: 82,
    disk: 68,
    issues: ["offline"],
  },
  {
    id: "pc-line-b-02",
    name: "PC-LINE-B-02",
    station: "UTPG3T00T01",
    bay: "FT1",
    status: "offline",
    model: "Lenovo ThinkCentre",
    ip: "10.177.23.108",
    mac: "2C-33-58-D9-93-A8",
    lastSeen: "07/01/2026, 05:41 AM",
    cpu: null,
    ram: null,
    disk: null,
    issues: ["offline"],
  },
  {
    id: "pc-qa-02",
    name: "PC-QA-02",
    station: "UTPG3TM0T01",
    bay: "FT1",
    status: "updating",
    model: "Dell OptiPlex 7010",
    ip: "10.177.23.109",
    mac: "2C-33-58-D9-93-A9",
    lastSeen: "07/01/2026, 10:27 AM",
    cpu: 50,
    ram: 65,
    disk: 70,
    issues: [],
  },
  {
    id: "ui-te-cft",
    name: "UI_TE_CFT",
    station: "UTPG3TM0T01",
    bay: "FT1",
    status: "online",
    model: "HP EliteDesk 800",
    ip: "10.177.23.231",
    mac: "2C-33-58-D9-93-C4",
    lastSeen: "07/01/2026, 10:28 AM",
    cpu: 2,
    ram: 81,
    disk: 38,
    issues: [],
  },
]

export const stats = [
  { key: "computers", label: "Computers", value: 10, token: "foreground" },
  { key: "active", label: "Active agents", value: 1, token: "success" },
  { key: "pending", label: "Pending", value: 0, token: "muted-foreground" },
  { key: "unassigned", label: "Unassigned", value: 0, token: "muted-foreground" },
  { key: "drifted", label: "Drifted", value: 0, token: "muted-foreground" },
  { key: "attention", label: "Needs attention", value: 7, token: "warning" },
]

export function needsAttention(list: Computer[]) {
  return list.filter((c) => c.issues.length > 0)
}
