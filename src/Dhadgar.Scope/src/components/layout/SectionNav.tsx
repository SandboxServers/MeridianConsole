import type { ScopeSectionInfo } from "../../lib/types";

interface SectionNavProps {
  section: ScopeSectionInfo;
  prevSection: ScopeSectionInfo | null;
  nextSection: ScopeSectionInfo | null;
  total: number;
}

export function SectionNav({ section, prevSection, nextSection, total }: SectionNavProps) {
  const progress = (section.number / total) * 100;

  return (
    <div className="mb-6 rounded-2xl border border-white/10 bg-white/5 p-4">
      {/* Breadcrumb */}
      <div className="flex items-center gap-2 text-sm text-white/60">
        <a href="/" className="hover:text-white transition-colors">
          Home
        </a>
        <span>/</span>
        <span className="text-white/80">{section.title}</span>
      </div>

      {/* Progress bar */}
      <div className="mt-3">
        <div className="flex items-center justify-between text-xs text-white/60">
          <span>
            Section {section.number} of {total}
          </span>
          <span>{Math.round(progress)}% complete</span>
        </div>
        <div className="mt-1 h-1 w-full overflow-hidden rounded-full bg-white/10">
          <div
            className="h-full rounded-full bg-indigo-500 transition-all"
            style={{ width: `${progress}%` }}
          />
        </div>
      </div>

      {/* Navigation */}
      <div className="mt-4 flex items-center justify-between gap-4">
        {prevSection ? (
          <a
            href={`/s/${prevSection.slug}`}
            className="flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-sm transition-colors hover:bg-white/10"
          >
            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M15 19l-7-7 7-7"
              />
            </svg>
            <span className="hidden sm:inline">{prevSection.title}</span>
            <span className="sm:hidden">Previous</span>
          </a>
        ) : (
          <div />
        )}

        {nextSection ? (
          <a
            href={`/s/${nextSection.slug}`}
            className="flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-sm transition-colors hover:bg-white/10"
          >
            <span className="hidden sm:inline">{nextSection.title}</span>
            <span className="sm:hidden">Next</span>
            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
            </svg>
          </a>
        ) : (
          <div />
        )}
      </div>
    </div>
  );
}
