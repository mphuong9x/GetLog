export type Station = {
  id: string
  name: string
  online: boolean
  pcCount: number
  schedule: string | null // null = "Anytime"
}

export type Model = {
  id: string
  code: string
  owner: string | null // null = "Unassigned"
  stations: Station[]
}

export type ProductionGroup = {
  id: string
  name: string
  models: Model[]
}

function s(
  id: string,
  name: string,
  online: boolean,
  pcCount: number,
  schedule: string | null = null,
): Station {
  return { id, name, online, pcCount, schedule }
}

export const productionGroups: ProductionGroup[] = [
  { id: 'ap', name: 'AP', models: [] },
  { id: 'camera', name: 'CAMERA', models: [] },
  {
    id: 'eot',
    name: 'EOT',
    models: [
      {
        id: 'upapt00t01',
        code: 'UPAPT00T01',
        owner: 'CFT',
        stations: [
          s('u1-1', 'DOWNLOAD', true, 1),
          s('u1-2', 'FT1', true, 2),
          s('u1-3', 'FT2', false, 1),
          s('u1-4', 'PT0', true, 1, '23:30–23:45 UTC · Sun'),
        ],
      },
      {
        id: 'utpg3t00t01',
        code: 'UTPG3T00T01',
        owner: null,
        stations: [
          s('u2-1', 'DOWNLOAD', true, 1),
          s('u2-2', 'FT1', false, 1),
        ],
      },
      {
        id: 'utpg3tm0t01',
        code: 'UTPG3TM0T01',
        owner: 'CFT',
        stations: [
          s('u3-1', 'DOWNLOAD', true, 1),
          s('u3-2', 'FT1', true, 3),
          s('u3-3', 'FT2', true, 1),
          s('u3-4', 'FT3', true, 1),
          s('u3-5', 'PT0', true, 1, '23:30–23:45 UTC · Sun'),
          s('u3-6', 'PT1', false, 1, '00:00–00:30 UTC · Mon'),
        ],
      },
    ],
  },
  { id: 'switch', name: 'SWITCH', models: [] },
]

export const workspaceStats = {
  groups: productionGroups.length,
  models: productionGroups.reduce((n, g) => n + g.models.length, 0),
  stations: productionGroups.reduce(
    (n, g) => n + g.models.reduce((m, model) => m + model.stations.length, 0),
    0,
  ),
}
