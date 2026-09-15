import { getSectores } from '../api/endpoints';
import { getSectoresByBranch, upsertSectores } from '../db/repositories';
import type { SectorRow } from '../db/schema';

export async function getOrFetchSectores(token: string, branchId: number): Promise<SectorRow[]> {
  const cached = await getSectoresByBranch(branchId);
  if (cached.length > 0) {
    return cached;
  }

  const fetched = await getSectores(token, branchId);
  const rows: SectorRow[] = fetched.map((s) => ({ id: s.id, branchId, name: s.name }));
  await upsertSectores(rows);
  return rows;
}
