import { useEffect, useState, type ReactNode } from 'react';
import { useTable, tableFeatures, type ColumnDef, type RowData } from '@tanstack/react-table';
import { ChevronLeft, ChevronRight, Search } from 'lucide-react';
import { Button, Card, Input } from './ui';
import { cn, number } from '@/lib/utils';

const features = tableFeatures({});

export interface Column<T> {
  id: string;
  header: ReactNode;
  cell: (row: T) => ReactNode;
  className?: string;
  /** Hidden below the md breakpoint so tables stay readable on phones. */
  hideOnMobile?: boolean;
}

/**
 * Server-paginated table built on TanStack Table. Rows are only the current page;
 * sorting and filtering happen on the server.
 */
export function DataTable<T extends RowData>({ columns, rows, rowKey, onRowClick, empty }: {
  columns: Column<T>[]; rows: T[]; rowKey: (row: T) => string; onRowClick?: (row: T) => void; empty?: ReactNode;
}) {
  const table = useTable({
    features,
    data: rows,
    getRowId: (row: T) => rowKey(row),
    columns: columns.map((c) => ({
      id: c.id,
      header: () => c.header,
      accessorFn: (row: T) => row,
      cell: (info: { row: { original: T } }) => c.cell(info.row.original),
    })) as unknown as ColumnDef<typeof features, T>[],
  });
  if (rows.length === 0) return <>{empty}</>;
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[640px] text-left text-sm">
        <thead>
          {table.getHeaderGroups().map((group) => (
            <tr key={group.id} className="border-b border-border bg-surface-2/60">
              {group.headers.map((header, i) => (
                <th key={header.id} scope="col" className={cn('px-4 py-2.5 text-xs font-medium text-muted', columns[i]?.hideOnMobile && 'hidden md:table-cell', columns[i]?.className)}>
                  <table.FlexRender header={header} />
                </th>
              ))}
            </tr>
          ))}
        </thead>
        <tbody>
          {table.getRowModel().rows.map((row) => (
            <tr
              key={row.id}
              onClick={onRowClick ? () => onRowClick(row.original) : undefined}
              onKeyDown={onRowClick ? (e) => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), onRowClick(row.original)) : undefined}
              tabIndex={onRowClick ? 0 : undefined}
              className={cn('border-b border-border last:border-0 transition-colors', onRowClick && 'cursor-pointer hover:bg-surface-2/70 focus:bg-surface-2 focus:outline-none')}
            >
              {row.getAllCells().map((cell, i) => (
                <td key={cell.id} className={cn('px-4 py-3 align-middle text-fg', columns[i]?.hideOnMobile && 'hidden md:table-cell', columns[i]?.className)}>
                  <table.FlexRender cell={cell} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (page: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-xs text-muted">
      <span>
        {number(from)}–{number(to)} of {number(total)}
      </span>
      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)} aria-label="Previous page">
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <span>
          Page {page} of {pages}
        </span>
        <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => onPage(page + 1)} aria-label="Next page">
          <ChevronRight className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}

/** Text search that waits for a pause in typing before updating. */
export function SearchInput({ value, onChange, placeholder, label }: { value: string; onChange: (value: string) => void; placeholder: string; label: string }) {
  const [text, setText] = useState(value);
  useEffect(() => setText(value), [value]);
  useEffect(() => {
    const timer = setTimeout(() => text !== value && onChange(text), 300);
    return () => clearTimeout(timer);
  }, [text, value, onChange]);
  return (
    <div className="relative min-w-0 flex-1 sm:max-w-xs">
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
      <Input aria-label={label} value={text} onChange={(e) => setText(e.target.value)} placeholder={placeholder} className="pl-9" />
    </div>
  );
}

export function TableCard({ toolbar, children, footer }: { toolbar?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <Card className="overflow-hidden animate-fade-up">
      {toolbar && <div className="flex flex-wrap items-center gap-2 border-b border-border px-4 py-3">{toolbar}</div>}
      {children}
      {footer}
    </Card>
  );
}

export function StatCard({ label, value, hint, icon, tone = 'primary', trend }: {
  label: string; value: ReactNode; hint?: ReactNode; icon: ReactNode; tone?: 'primary' | 'teal' | 'warning' | 'danger' | 'info'; trend?: ReactNode;
}) {
  const toneClass = { primary: 'bg-primary-soft text-primary', teal: 'bg-teal-soft text-teal', warning: 'bg-warning-soft text-warning', danger: 'bg-danger-soft text-danger', info: 'bg-info-soft text-info' }[tone];
  return (
    <Card className="p-4 transition-shadow hover:shadow-lg animate-fade-up">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-medium text-muted">{label}</p>
          <p className="mt-1.5 text-2xl font-semibold tracking-tight text-fg tabular-nums">{value}</p>
        </div>
        <div className={cn('rounded-lg p-2', toneClass)}>{icon}</div>
      </div>
      {(hint || trend) && (
        <div className="mt-2 flex items-center justify-between gap-2 text-xs text-muted">
          <span className="truncate">{hint}</span>
          {trend}
        </div>
      )}
    </Card>
  );
}

export function Timeline({ items }: { items: { key: string; title: ReactNode; meta: ReactNode; detail?: ReactNode; tone?: 'success' | 'danger' | 'neutral' }[] }) {
  return (
    <ol className="relative space-y-4 border-l border-border pl-5">
      {items.map((item) => (
        <li key={item.key} className="relative">
          <span className={cn('absolute -left-[25px] top-1.5 h-2.5 w-2.5 rounded-full ring-4 ring-surface', item.tone === 'danger' ? 'bg-danger' : item.tone === 'success' ? 'bg-primary' : 'bg-muted')} />
          <p className="text-sm text-fg">{item.title}</p>
          <p className="text-xs text-muted">{item.meta}</p>
          {item.detail && <p className="mt-1 text-xs text-muted">{item.detail}</p>}
        </li>
      ))}
    </ol>
  );
}
