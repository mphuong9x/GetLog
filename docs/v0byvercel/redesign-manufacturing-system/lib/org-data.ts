export type Member = {
  id: string
  name: string
  handle: string
  role: "leader" | "member"
  title: string
  status: "online" | "offline"
}

export type Team = {
  id: string
  name: string
  purpose: string
  leaders: Member[]
  members: Member[]
}

export type Department = {
  id: string
  code: string
  name: string
  productionGroups: number
  teams: Team[]
}

function m(
  id: string,
  name: string,
  handle: string,
  role: Member["role"],
  title: string,
  status: Member["status"] = "offline",
): Member {
  return { id, name, handle, role, title, status }
}

export const departments: Department[] = [
  {
    id: "ap",
    code: "AP",
    name: "Assembly & Packaging",
    productionGroups: 0,
    teams: [
      {
        id: "ap-cft",
        name: "CFT",
        purpose: "Cross-functional line support",
        leaders: [m("u1", "System Admin", "admin", "leader", "Line Supervisor", "online")],
        members: [
          m("u1", "System Admin", "admin", "member", "Line Supervisor", "online"),
          m("u2", "Member Test 1", "v000111004", "member", "Assembly Operator"),
        ],
      },
      {
        id: "ap-online",
        name: "ONLINE",
        purpose: "Real-time production monitoring",
        leaders: [m("u3", "Dao Van Minh", "v000111210", "leader", "Shift Lead", "online")],
        members: [
          m("u3", "Dao Van Minh", "v000111210", "member", "Shift Lead", "online"),
          m("u4", "Tran Thi Ha", "v000111345", "member", "QA Inspector"),
          m("u5", "Le Quoc Bao", "v000111588", "member", "Line Operator", "online"),
        ],
      },
    ],
  },
  {
    id: "camera",
    code: "CAMERA",
    name: "Camera Module",
    productionGroups: 3,
    teams: [
      {
        id: "cam-cal",
        name: "CALIBRATION",
        purpose: "Optical calibration cell",
        leaders: [m("u6", "Nguyen Van An", "v000112001", "leader", "Calibration Lead")],
        members: [
          m("u6", "Nguyen Van An", "v000112001", "member", "Calibration Lead"),
          m("u7", "Pham Minh Tu", "v000112044", "member", "Optics Technician", "online"),
        ],
      },
      {
        id: "cam-test",
        name: "TESTING",
        purpose: "Functional test & burn-in",
        leaders: [m("u8", "Hoang Thi Lan", "v000112110", "leader", "Test Engineer", "online")],
        members: [
          m("u8", "Hoang Thi Lan", "v000112110", "member", "Test Engineer", "online"),
          m("u9", "Vu Dinh Nam", "v000112187", "member", "Test Operator"),
        ],
      },
    ],
  },
  {
    id: "eot",
    code: "EOT",
    name: "End-of-Line Test",
    productionGroups: 5,
    teams: [
      {
        id: "eot-day",
        name: "DAY SHIFT",
        purpose: "Primary EOL verification",
        leaders: [m("u10", "Bui Van Khoa", "v000113001", "leader", "EOL Supervisor", "online")],
        members: [
          m("u10", "Bui Van Khoa", "v000113001", "member", "EOL Supervisor", "online"),
          m("u11", "Do Thi Mai", "v000113077", "member", "Test Operator"),
          m("u12", "Ngo Van Hai", "v000113102", "member", "Data Analyst", "online"),
          m("u13", "Ly Thanh Son", "v000113155", "member", "Line Operator"),
        ],
      },
      {
        id: "eot-night",
        name: "NIGHT SHIFT",
        purpose: "Overnight EOL coverage",
        leaders: [m("u14", "Trinh Van Duc", "v000113200", "leader", "Night Lead")],
        members: [
          m("u14", "Trinh Van Duc", "v000113200", "member", "Night Lead"),
          m("u15", "Cao Thi Yen", "v000113244", "member", "Test Operator", "online"),
        ],
      },
    ],
  },
  {
    id: "switch",
    code: "SWITCH",
    name: "Switch & Interconnect",
    productionGroups: 2,
    teams: [
      {
        id: "sw-smt",
        name: "SMT",
        purpose: "Surface-mount assembly",
        leaders: [m("u16", "Dang Van Tho", "v000114001", "leader", "SMT Lead", "online")],
        members: [
          m("u16", "Dang Van Tho", "v000114001", "member", "SMT Lead", "online"),
          m("u17", "Phan Thi Nga", "v000114066", "member", "Machine Operator"),
        ],
      },
      {
        id: "sw-final",
        name: "FINAL",
        purpose: "Final assembly & inspection",
        leaders: [m("u18", "Ta Quang Huy", "v000114120", "leader", "Assembly Lead")],
        members: [
          m("u18", "Ta Quang Huy", "v000114120", "member", "Assembly Lead"),
          m("u19", "Ho Thi Thu", "v000114188", "member", "Inspector", "online"),
        ],
      },
    ],
  },
]

export function departmentStats(dept: Department) {
  const users = new Set<string>()
  for (const team of dept.teams) {
    for (const person of [...team.leaders, ...team.members]) users.add(person.id)
  }
  return { teamCount: dept.teams.length, userCount: users.size }
}
