import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Archive, Pencil, Plus, RotateCcw, UtensilsCrossed } from 'lucide-react';
import { api, qs } from '@/api/client';
import type { FoodRow } from '@/api/types';
import { Badge, Button, EmptyState, ErrorState, Field, Input, LoadingState, PageHeader, Select, Switch } from '@/components/ui';
import { DataTable, Pagination, SearchInput, TableCard, type Column } from '@/components/data';
import { ConfirmDialog, Modal } from '@/components/overlays';
import { useAction, useUrlState } from '@/lib/hooks';
import { useAuth } from '@/providers/auth';
import { number, relative } from '@/lib/utils';

const allergenTone = { Unknown: 'warning', NoneDeclared: 'neutral', Declared: 'danger' } as const;
type FoodForm = Omit<FoodRow, 'id' | 'source' | 'allergenStatus' | 'isArchived' | 'lastUpdatedAtUtc'>;
const emptyFood: FoodForm = { name: '', nameTamil: null, nameSinhala: null, category: null, servingSizeGrams: 100, calories: 0, proteinGrams: 0, carbsGrams: 0, fatGrams: 0, fiberGrams: 0, sugarGrams: 0, sodiumMilligrams: 0, allergens: null, isVerified: false };

export default function FoodsPage() {
  const { can } = useAuth();
  const [filters, setFilters] = useUrlState({ search: '', source: '', archived: 'false', sort: 'name', page: '1' });
  const [editing, setEditing] = useState<FoodRow | 'new' | null>(null);
  const [archiving, setArchiving] = useState<FoodRow | null>(null);
  const list = useQuery({
    queryKey: ['foods', filters],
    queryFn: () => api<{ total: number; page: number; pageSize: number; sources: string[]; items: FoodRow[] }>(`/api/admin/foods${qs({ ...filters, sort: filters.sort === 'name' ? undefined : filters.sort, pageSize: 25 })}`),
    placeholderData: (p) => p,
  });
  const toggleArchive = useAction((f: FoodRow) => api(`/api/admin/foods/${f.id}/${f.isArchived ? 'restore' : 'archive'}`, { method: 'POST' }), { success: (_r) => 'Food updated.', invalidate: [['foods']], silentError: true });
  const manage = can('foods.manage');

  const columns: Column<FoodRow>[] = [
    { id: 'name', header: 'Food', cell: (f) => <div className="min-w-0"><p className="truncate font-medium">{f.name} {f.isVerified && <Badge tone="success" className="ml-1">Verified</Badge>}</p><p className="truncate text-xs text-muted">{[f.nameTamil, f.nameSinhala].filter(Boolean).join(' · ') || f.category || '—'}</p></div> },
    { id: 'source', header: 'Source', cell: (f) => <span className="text-xs">{f.source}</span>, hideOnMobile: true },
    { id: 'serving', header: 'Serving', cell: (f) => <span className="tabular-nums text-xs">{number(f.servingSizeGrams)} g</span>, hideOnMobile: true },
    { id: 'kcal', header: 'kcal', cell: (f) => <span className="tabular-nums">{number(f.calories, 1)}</span>, className: 'text-right' },
    { id: 'macros', header: 'P / C / F (g)', cell: (f) => <span className="tabular-nums text-xs">{number(f.proteinGrams, 1)} / {number(f.carbsGrams, 1)} / {number(f.fatGrams, 1)}</span>, hideOnMobile: true },
    { id: 'allergens', header: 'Allergens', cell: (f) => <Badge tone={allergenTone[f.allergenStatus]}>{f.allergenStatus === 'Declared' ? f.allergens : f.allergenStatus === 'Unknown' ? 'Unknown' : 'None declared'}</Badge>, hideOnMobile: true },
    { id: 'updated', header: 'Updated', cell: (f) => <span className="text-xs text-muted">{relative(f.lastUpdatedAtUtc)}</span>, hideOnMobile: true },
    {
      id: 'actions', header: <span className="sr-only">Actions</span>, className: 'text-right',
      cell: (f) => manage && (
        <div className="flex justify-end gap-1">
          {!f.isArchived && <Button size="icon" variant="ghost" aria-label={`Edit ${f.name}`} onClick={() => setEditing(f)}><Pencil className="h-4 w-4" /></Button>}
          <Button size="icon" variant="ghost" aria-label={f.isArchived ? `Restore ${f.name}` : `Archive ${f.name}`} onClick={() => (f.isArchived ? toggleArchive.mutate(f) : setArchiving(f))}>
            {f.isArchived ? <RotateCcw className="h-4 w-4" /> : <Archive className="h-4 w-4" />}
          </Button>
        </div>
      ),
    },
  ];

  return (
    <>
      <PageHeader
        title="Food catalogue"
        icon={<UtensilsCrossed className="h-5 w-5" />}
        description="Shared foods users can log, per serving. “Unknown” allergens means the source didn't say — it is never treated as allergen-free. Verified foods are protected from USDA sync overwrites. Users' private foods are not listed."
        actions={manage && <Button onClick={() => setEditing('new')}><Plus className="h-4 w-4" /> Add food</Button>}
      />
      <TableCard
        toolbar={
          <>
            <SearchInput label="Search foods" placeholder="Search English, Tamil or Sinhala names" value={filters.search} onChange={(search) => setFilters({ search })} />
            <Select aria-label="Source" value={filters.source} onChange={(e) => setFilters({ source: e.target.value })} className="w-44">
              <option value="">All sources</option>
              {list.data?.sources.map((s) => <option key={s}>{s}</option>)}
            </Select>
            <Select aria-label="Sort" value={filters.sort} onChange={(e) => setFilters({ sort: e.target.value })} className="w-40">
              <option value="name">Name</option><option value="calories">Calories</option><option value="protein">Protein</option><option value="updated">Recently updated</option>
            </Select>
            <Select aria-label="Archived" value={filters.archived} onChange={(e) => setFilters({ archived: e.target.value })} className="w-36">
              <option value="false">Active</option><option value="true">Archived</option>
            </Select>
          </>
        }
        footer={list.data && <Pagination page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(page) => setFilters({ page: String(page) })} />}
      >
        {list.isPending ? <LoadingState /> : list.isError ? <ErrorState error={list.error} onRetry={() => void list.refetch()} /> : (
          <DataTable columns={columns} rows={list.data.items} rowKey={(f) => f.id} empty={<EmptyState title="No foods match" />} />
        )}
      </TableCard>
      {editing && <FoodDialog food={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      <ConfirmDialog open={!!archiving} onOpenChange={(o) => !o && setArchiving(null)} title={`Archive ${archiving?.name}?`} description="Users can no longer find or log it. Past meal logs keep their values. You can restore it later." confirmLabel="Archive" tone="danger" onConfirm={() => toggleArchive.mutateAsync(archiving!)} />
    </>
  );
}

function FoodDialog({ food, onClose }: { food: FoodRow | null; onClose: () => void }) {
  const [form, setForm] = useState<FoodForm>(food ? { ...emptyFood, ...food } : emptyFood);
  const [allergenKnown, setAllergenKnown] = useState(food ? food.allergenStatus !== 'Unknown' : false);
  const [error, setError] = useState<string>();
  const set = (patch: Partial<FoodForm>) => setForm((f) => ({ ...f, ...patch }));
  const save = useAction(
    () => {
      const body = { ...form, allergens: allergenKnown ? (form.allergens ?? '').trim() : null };
      return food ? api(`/api/admin/foods/${food.id}`, { method: 'PUT', body }) : api('/api/admin/foods', { body });
    },
    { success: food ? 'Food updated.' : 'Food added.', invalidate: [['foods']], silentError: true },
  );
  const numberField = (key: keyof FoodForm, label: string, max: number) => (
    <Field label={label} htmlFor={`food-${key}`}>
      <Input id={`food-${key}`} type="number" min={0} max={max} step="0.1" value={String(form[key] ?? 0)} onChange={(e) => set({ [key]: Number(e.target.value) } as Partial<FoodForm>)} />
    </Field>
  );
  return (
    <Modal
      open
      wide
      onOpenChange={(o) => !o && onClose()}
      title={food ? `Edit ${food.name}` : 'Add a food'}
      description="Values are per serving. Changes are audited."
      footer={<><Button variant="outline" onClick={onClose}>Cancel</Button><Button loading={save.isPending} disabled={!form.name.trim()} onClick={() => save.mutate(undefined, { onSuccess: onClose, onError: (e) => setError(e.message) })}>Save</Button></>}
    >
      <div className="grid gap-4 sm:grid-cols-3">
        <div className="sm:col-span-3"><Field label="Name (English)" htmlFor="food-name"><Input id="food-name" maxLength={200} value={form.name} onChange={(e) => set({ name: e.target.value })} /></Field></div>
        <Field label="Tamil name" htmlFor="food-ta"><Input id="food-ta" lang="ta" value={form.nameTamil ?? ''} onChange={(e) => set({ nameTamil: e.target.value || null })} /></Field>
        <Field label="Sinhala name" htmlFor="food-si"><Input id="food-si" lang="si" value={form.nameSinhala ?? ''} onChange={(e) => set({ nameSinhala: e.target.value || null })} /></Field>
        <Field label="Category" htmlFor="food-cat"><Input id="food-cat" value={form.category ?? ''} onChange={(e) => set({ category: e.target.value || null })} /></Field>
        {numberField('servingSizeGrams', 'Serving (g)', 2000)}
        {numberField('calories', 'Calories (kcal)', 5000)}
        {numberField('proteinGrams', 'Protein (g)', 500)}
        {numberField('carbsGrams', 'Carbohydrate (g)', 1000)}
        {numberField('fatGrams', 'Fat (g)', 500)}
        {numberField('fiberGrams', 'Fibre (g)', 200)}
        {numberField('sugarGrams', 'Sugar (g)', 500)}
        {numberField('sodiumMilligrams', 'Sodium (mg)', 20000)}
        <div className="space-y-3 sm:col-span-3">
          <Switch label="Allergen information is known" checked={allergenKnown} onChange={setAllergenKnown} />
          {allergenKnown && <Field label="Allergens (comma-separated; leave empty if none)" htmlFor="food-allergens"><Input id="food-allergens" value={form.allergens ?? ''} onChange={(e) => set({ allergens: e.target.value })} placeholder="Peanuts, Milk" /></Field>}
          <Switch label="Verified (protect from USDA sync overwrites)" checked={form.isVerified} onChange={(v) => set({ isVerified: v })} />
        </div>
        {error && <p className="text-sm text-danger sm:col-span-3" role="alert">{error}</p>}
      </div>
    </Modal>
  );
}
