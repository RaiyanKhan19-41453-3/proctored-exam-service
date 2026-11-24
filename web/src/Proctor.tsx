import { HubConnectionBuilder, type HubConnection } from '@microsoft/signalr';
import { useEffect, useRef, useState } from 'react';

type LiveEvent = { attemptId: string; examId: string; type: string; severity: string; at: string; detail?: string };
type Flag = { id: string; kind: number; score: number; detailJson?: string; reviewed: boolean };

const KINDS = ['AnswerSimilarity', 'SharedDevice', 'SharedIp', 'GeoAnomaly', 'KioskRequired'];

export default function Proctor({ token, examId }: { token: string; examId: string }) {
  const [events, setEvents] = useState<LiveEvent[]>([]);
  const [flags, setFlags] = useState<Flag[]>([]);
  const [status, setStatus] = useState('connecting…');
  const [evidence, setEvidence] = useState<{ id: string; kind: number; url: string }[]>([]);
  const [selected, setSelected] = useState('');
  const hub = useRef<HubConnection | null>(null);

  const loadFlags = async () => {
    const r = await fetch(`/api/proctor/exams/${examId}/flags`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (r.ok) setFlags(await r.json() as Flag[]);
  };

  const loadEvidence = async (attemptId: string) => {
    setSelected(attemptId);
    const r = await fetch(`/api/evidence/attempts/${attemptId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!r.ok) return;
    const rows = await r.json() as { id: string; kind: number }[];
    const withUrls = await Promise.all(rows.slice(0, 40).map(async row => {
      const u = await fetch(`/api/evidence/${row.id}/url`, { headers: { Authorization: `Bearer ${token}` } });
      const data = u.ok ? await u.json() as { url: string } : { url: '' };
      return { id: row.id, kind: row.kind, url: data.url };
    }));
    setEvidence(withUrls);
  };

  const loadHistory = async () => {
    const r = await fetch(`/api/proctor/exams/${examId}/events`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (r.ok) setEvents(await r.json() as LiveEvent[]);
  };

  useEffect(() => {
    if (!examId) return;
    void loadFlags(); void loadHistory();
    const c = new HubConnectionBuilder()
      .withUrl(`/hubs/proctor?examId=${examId}`, { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .build();
    c.on('proctor-event', (e: LiveEvent) => setEvents(prev => [e, ...prev].slice(0, 100)));
    c.on('anomaly-flag', () => void loadFlags());
    c.start().then(() => setStatus('live')).catch(() => setStatus('offline'));
    hub.current = c;
    return () => { void c.stop(); hub.current = null; };
  }, [examId, token]);

  return (
    <section className="card">
      <div className="row"><h2>Live proctor</h2><span className={`badge ${status}`}>{status}</span></div>
      <p className="muted">Events and device/answer anomalies arrive in real time during the exam.</p>
      <h3>Flags</h3>
      {flags.length === 0 ? <p className="muted">No anomalies detected.</p> : flags.map(f => (
        <div key={f.id} className="report">
          <strong>{KINDS[f.kind] ?? f.kind}</strong> — score {f.score} {f.reviewed ? '(reviewed)' : ''}
        </div>
      ))}
      <h3>Live events</h3>
      {events.slice(0, 30).map((e, i) => (
        <div key={i} className="report">
          {e.severity} · {e.type} · {e.attemptId.slice(0, 8)}
          <button onClick={() => void loadEvidence(e.attemptId)}>Evidence</button>
        </div>
      ))}
      {selected && (
        <div className="card">
          <h3>Evidence for attempt {selected.slice(0, 8)}</h3>
          {evidence.length === 0 ? <p className="muted">No evidence yet.</p> : evidence.map(e => (
            e.kind === 0 || e.kind === 4 ? <img key={e.id} src={e.url} alt="frame" style={{ width: 160, margin: 4 }} />
                                        : <a key={e.id} href={e.url} target="_blank" rel="noreferrer">Open recording #{e.kind}</a>
          ))}
        </div>
      )}
    </section>
  );
}
