import { computed, Injectable, isDevMode, signal } from '@angular/core';
import type { RxCollection, RxJsonSchema, RxStorage } from 'rxdb';

export interface HiddenJob {
  id: string;
  workingTitle: string;
  agency: string;
  /** ISO timestamp. */
  hiddenAt: string;
  reason: 'reported';
}

const schema: RxJsonSchema<HiddenJob> = {
  version: 0,
  primaryKey: 'id',
  type: 'object',
  properties: {
    id: { type: 'string', maxLength: 64 },
    workingTitle: { type: 'string', maxLength: 200 },
    agency: { type: 'string', maxLength: 200 },
    hiddenAt: { type: 'string', format: 'date-time', maxLength: 40 },
    reason: { type: 'string', enum: ['reported'], maxLength: 16 },
  },
  required: ['id', 'workingTitle', 'agency', 'hiddenAt', 'reason'],
  indexes: ['hiddenAt'],
};

/**
 * Posts the visitor chose to hide, kept only in this browser with RxDB (IndexedDB via Dexie).
 * Their IDs are sent with each search so results, counts and pages leave them out; the
 * server never stores them. Other open tabs stay in sync through RxDB's multi-instance support.
 * RxDB is loaded on demand so it stays out of the initial bundle.
 */
@Injectable({ providedIn: 'root' })
export class HiddenJobsStore {
  private collection: RxCollection<HiddenJob> | null = null;
  private readonly ready = signal(false);

  /** Newest first. */
  readonly hidden = signal<HiddenJob[]>([]);

  /** Null until the database has loaded, so the first search can wait for it. */
  readonly ids = computed(() => (this.ready() ? this.hidden().map((h) => h.id) : null));

  /** True when IndexedDB was unavailable and hiding only lasts for this visit. */
  readonly sessionOnly = signal(false);

  constructor() {
    void this.open();
  }

  isHidden(id: string): boolean {
    return this.hidden().some((h) => h.id === id);
  }

  async hide(job: { id: string; workingTitle: string; agency: string }): Promise<void> {
    await this.collection?.upsert({
      id: job.id,
      workingTitle: job.workingTitle.slice(0, 200),
      agency: job.agency.slice(0, 200),
      hiddenAt: new Date().toISOString(),
      reason: 'reported',
    });
  }

  async unhide(id: string): Promise<void> {
    await this.collection?.findOne(id).remove();
  }

  async unhideAll(): Promise<void> {
    await this.collection?.find().remove();
  }

  private async open(): Promise<void> {
    try {
      const { getRxStorageDexie } = await import('rxdb/plugins/storage-dexie');
      this.collection = await this.create('fedtech', getRxStorageDexie());
    } catch (error) {
      // IndexedDB can be blocked (private windows, strict settings). Hiding still works for this visit.
      console.warn('Hidden posts will not be saved in this browser:', error);
      this.sessionOnly.set(true);
      try {
        const { getRxStorageMemory } = await import('rxdb/plugins/storage-memory');
        this.collection = await this.create('fedtech-session', getRxStorageMemory());
      } catch (memoryError) {
        console.error('Could not open the hidden-posts store:', memoryError);
        this.ready.set(true);
        return;
      }
    }

    this.collection.find({ sort: [{ hiddenAt: 'desc' }] }).$.subscribe((docs) => {
      this.hidden.set(docs.map((d) => d.toJSON() as HiddenJob));
      this.ready.set(true);
    });
  }

  private async create(name: string, storage: RxStorage<any, any>): Promise<RxCollection<HiddenJob>> {
    const { addRxPlugin, createRxDatabase } = await import('rxdb');
    if (isDevMode()) {
      // Dev only (never loaded in production): strict checks on schemas and queries. Dev mode
      // requires a schema validator on the storage, so wrap it with ajv here too.
      const [{ RxDBDevModePlugin }, { wrappedValidateAjvStorage }] = await Promise.all([
        import('rxdb/plugins/dev-mode'),
        import('rxdb/plugins/validate-ajv'),
      ]);
      addRxPlugin(RxDBDevModePlugin);
      storage = wrappedValidateAjvStorage({ storage });
    }
    const db = await createRxDatabase({ name, storage, multiInstance: true, closeDuplicates: true });
    const { hiddenjobs } = await db.addCollections({ hiddenjobs: { schema } });
    return hiddenjobs as RxCollection<HiddenJob>;
  }
}
