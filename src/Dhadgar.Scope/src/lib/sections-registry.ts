import type { ScopeSectionInfo } from './types';

export const sections: ScopeSectionInfo[] = [
  { number: 1, title: 'Project Structure', slug: 'project-structure' },
  { number: 2, title: 'Vision & Scope', slug: 'vision' },
  { number: 3, title: 'Build Strategy', slug: 'build-strategy' },
  { number: 4, title: 'Tech Stack', slug: 'tech-stack' },
  { number: 5, title: 'Deployment Topology', slug: 'deployment' },
  { number: 6, title: 'Data Retention', slug: 'data-retention' },
  { number: 7, title: 'Security Architecture', slug: 'security' },
  { number: 8, title: 'Certificate Management', slug: 'certificates' },
  { number: 9, title: 'System Architecture', slug: 'architecture' },
  { number: 10, title: 'Database Schemas', slug: 'database-schemas' },
  { number: 11, title: 'Network Flows', slug: 'flows' },
  { number: 12, title: 'Service Communication Matrix', slug: 'matrix' },
  { number: 13, title: 'RabbitMQ Topology', slug: 'rabbitmq' },
  { number: 14, title: 'Services Catalogue', slug: 'services' },
  { number: 15, title: 'Agent Architecture', slug: 'agents' },
  { number: 16, title: 'KiP Edition', slug: 'kip' },
  { number: 17, title: 'MVP Scope & Phases', slug: 'mvp' },
  { number: 18, title: 'Product Governance', slug: 'governance' },
  { number: 19, title: 'Repository Structure', slug: 'repos' },
];

export function findBySlug(slug: string): ScopeSectionInfo | undefined {
  return sections.find(s => s.slug.toLowerCase() === slug.toLowerCase());
}

export function getAllSections(): ScopeSectionInfo[] {
  return sections;
}
