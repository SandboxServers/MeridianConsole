import type {
  DependencyGraphData,
  ArchitectureGraphData,
  DbSchemaCatalog,
  CommMatrixData,
} from "./types";

export async function getDependencyGraph(): Promise<DependencyGraphData> {
  const res = await fetch("/content/dependencies.json");
  return res.json();
}

export async function getArchitectureGraph(): Promise<ArchitectureGraphData> {
  const res = await fetch("/content/architecture-park.v1.json");
  return res.json();
}

export async function getDbSchemaCatalog(): Promise<DbSchemaCatalog> {
  const res = await fetch("/content/db-schemas.v1.json");
  return res.json();
}

export async function getCommMatrix(): Promise<CommMatrixData> {
  const res = await fetch("/content/comm-matrix.v1.json");
  return res.json();
}
