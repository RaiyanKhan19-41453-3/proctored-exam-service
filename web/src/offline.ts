// Durable local queue for autosaves when a home connection drops.
type Pending = { id: string; path: string; body: unknown; created: number };
const KEY = 'exam-pending';

export function enqueue(path: string, body: unknown) {
  const items = load();
  items.push({ id: crypto.randomUUID(), path, body, created: Date.now() });
  localStorage.setItem(KEY, JSON.stringify(items));
}

function load(): Pending[] {
  try { return JSON.parse(localStorage.getItem(KEY) ?? '[]') as Pending[]; }
  catch { return []; }
}

export async function flush(token: string): Promise<number> {
  if (!navigator.onLine) return 0;
  const items = load();
  if (!items.length) return 0;
  const remaining: Pending[] = [];
  let sent = 0;
  for (const item of items) {
    try {
      const r = await fetch(item.path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify(item.body),
      });
      if (r.ok) sent++; else remaining.push(item);
    } catch { remaining.push(item); }
  }
  localStorage.setItem(KEY, JSON.stringify(remaining));
  return sent;
}

export function pendingCount() { return load().length; }
