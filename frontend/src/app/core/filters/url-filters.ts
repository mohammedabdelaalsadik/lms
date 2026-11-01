import { Signal, WritableSignal, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

export interface UrlFilters<K extends string> {
  values: Record<K, WritableSignal<string>>;
  page: Signal<number>;
  activeCount: Signal<number>;
  set(key: K, value: string): void;
  setPage(index: number): void;
  clear(): void;
}

/**
 * Keeps filters and paging in the address bar, so a filtered view survives a refresh,
 * works with the back button and can be shared as a link.
 */
export function urlFilters<K extends string>(defaults: Record<K, string>): UrlFilters<K> {
  const router = inject(Router);
  const route = inject(ActivatedRoute);
  const query = route.snapshot.queryParamMap;
  const keys = Object.keys(defaults) as K[];

  const values = Object.fromEntries(keys.map((k) => [k, signal(query.get(k) ?? defaults[k])])) as Record<K, WritableSignal<string>>;
  const page = signal(Math.max(0, Number(query.get('page')) || 0));
  let first = true;

  effect(() => {
    const params: Record<string, string | null> = { page: page() > 0 ? String(page()) : null };
    keys.forEach((k) => (params[k] = values[k]() === defaults[k] ? null : values[k]()));

    if (first) {
      first = false;
      return;
    }

    void router.navigate([], { relativeTo: route, queryParams: params, queryParamsHandling: 'merge', replaceUrl: true });
  });

  return {
    values,
    page,
    activeCount: computed(() => keys.filter((k) => values[k]() !== defaults[k]).length),
    set(key, value) {
      page.set(0);
      values[key].set(value);
    },
    setPage(index) {
      page.set(index);
    },
    clear() {
      page.set(0);
      keys.forEach((k) => values[k].set(defaults[k]));
    },
  };
}
