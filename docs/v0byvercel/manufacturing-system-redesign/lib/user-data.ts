export type RoleAssignment = {
  id: string
  role: string
  scopeType: "Global" | "Team"
  scopeName?: string
  effectiveFrom: string
  effectiveTo: string
  assignedAt: string
  assignedBy: string
}

export type UserRecord = {
  fullName: string
  initials: string
  email: string
  ecode: string
  status: "Active" | "Suspended" | "Inactive"
  lastActive: string
  createdAt: string
  teams: string[]
  roles: RoleAssignment[]
}

export const user: UserRecord = {
  fullName: "System Admin",
  initials: "SA",
  email: "admin@system.local",
  ecode: "ADMIN",
  status: "Active",
  lastActive: "Today, 14:52",
  createdAt: "10/06/2026",
  teams: ["EOT-CFT", "SWITCH-CFT", "EOT-ONLINE", "AP-CFT"],
  roles: [
    {
      id: "r1",
      role: "TeamLeader",
      scopeType: "Team",
      scopeName: "AP-CFT",
      effectiveFrom: "now",
      effectiveTo: "∞",
      assignedAt: "26/06/2026 16:23",
      assignedBy: "j.harris",
    },
    {
      id: "r2",
      role: "TeamLeader",
      scopeType: "Team",
      scopeName: "EOT-ONLINE",
      effectiveFrom: "now",
      effectiveTo: "∞",
      assignedAt: "26/06/2026 10:37",
      assignedBy: "j.harris",
    },
    {
      id: "r3",
      role: "Admin",
      scopeType: "Global",
      effectiveFrom: "now",
      effectiveTo: "∞",
      assignedAt: "10/06/2026 16:08",
      assignedBy: "system",
    },
  ],
}
