// Section types
export interface ScopeSectionInfo {
  number: number;
  title: string;
  slug: string;
}

// Dependency Graph types
export interface DependencyGraphData {
  layers: string[];
  nodes: DependencyNode[];
  edges: DependencyEdge[];
}

export interface DependencyNode {
  id: string;
  name: string;
  emoji?: string;
  port?: number;
  layer: string;
  description?: string;
  responsibilities: string[];
  dependencies: string[];
  dependents: string[];
  endpoints: string[];
}

export interface DependencyEdge {
  id: string;
  source: string;
  target: string;
  relationship: string;
}

// Architecture Graph types
export interface ArchitectureGraphData {
  version: string;
  districts: ArchitectureDistrict[];
  nodes: ArchitectureNode[];
  edges: ArchitectureEdge[];
  tours: ArchitectureTour[];
}

export interface ArchitectureDistrict {
  id: string;
  name: string;
  center: ArchitecturePoint;
}

export interface ArchitecturePoint {
  x: number;
  y: number;
}

export interface ArchitectureNode {
  id: string;
  name: string;
  district: string;
  kind: "service" | "agent" | "db" | "foundation" | "external" | "client";
  emoji: string;
  description: string;
  position: ArchitecturePoint;
  ports: string[];
  endpoints: string[];
  responsibilities: string[];
}

export interface ArchitectureEdge {
  id: string;
  source: string;
  target: string;
  kind: "http" | "ws" | "amqp" | "db" | "dns" | "other";
  label: string;
}

export interface ArchitectureTour {
  id: string;
  name: string;
  steps: ArchitectureTourStep[];
}

export interface ArchitectureTourStep {
  title: string;
  body: string;
  focusNodes: string[];
  focusEdges: string[];
}

// Database Schema types
export interface DbSchemaCatalog {
  version: string;
  notes: string[];
  services: DbServiceSchema[];
}

export interface DbServiceSchema {
  key: string;
  name: string;
  schema: string;
  items: DbSchemaItem[];
  relationships: DbRelationship[];
}

export interface DbSchemaItem {
  id: string;
  kind: "table" | "view" | "function" | "enum" | "type";
  details: string[];
}

export interface DbRelationship {
  from: string;
  to: string;
  label: string;
}

// Communication Matrix types
export interface CommMatrixData {
  headers: string[];
  rows: CommMatrixRow[];
}

export interface CommMatrixRow {
  from: string;
  cells: CommMatrixCell[];
}

export interface CommMatrixCell {
  to: string;
  value: string;
}
