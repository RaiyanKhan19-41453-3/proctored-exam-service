import { useCallback, useEffect, useRef, useState } from 'react';

// Must match ExamService.Domain.Common.EventType / Severity on the server.
const EV = { TabBlur: 2, Copy: 3, Paste: 4, FullscreenExit: 6, CameraBlocked: 7,
  MicBlocked: 8, FaceAbsent: 9, MultipleFaces: 10, FaceCovered: 11, AudioAnomaly: 12,
  Disconnected: 13, Reconnected: 14, ForcedSubmit: 15 } as const;
const SEV = { Low: 2, Medium: 3, High: 4, Critical: 5 } as const;


type Question = { id: string; type: number; text: string; optionsJson?: string; marks: number };
type Exam = { id: string; title: string; instructions?: string; durationMinutes: number; requireCamera: boolean; requireMicrophone: boolean; lockTab: boolean; disableCopyPaste: boolean; questions: Question[] };
type Report = { id: string; candidateName: string; totalScore: number | null; integrityScore: number; decision: number; violationCount: number };

const API = '/api';

export default function App() {
  const [token, setToken] = useState(() => localStorage.getItem('exam-token') ?? '');
  const [secret, setSecret] = useState('');
  const [examId, setExamId] = useState('');
  const [exam, setExam] = useState<Exam | null>(null);
  const [attemptId, setAttemptId] = useState('');
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [reports, setReports] = useState<Report[]>([]);
  const [error, setError] = useState('');
  const [violations, setViolations] = useState(0);
  const videoRef = useRef<HTMLVideoElement>(null);
  const streamRef = useRef<MediaStream | null>(null);

  const headers = () => ({ 'Content-Type': 'application/json', Authorization: `Bearer ${token}` });

  const report = useCallback(async (type: number, severity: number, detail?: unknown) => {
    setViolations(v => v + 1);
    if (!attemptId) return;
    try {
      await fetch(`${API}/attempts/${attemptId}/events`, {
        method: 'POST', headers: headers(),
        body: JSON.stringify({ type, severity, detailJson: detail ? JSON.stringify(detail) : undefined }),
      });
    } catch { /* offline: will retry on next action */ }
  }, [attemptId, token]);

  useEffect(() => {
    if (!exam) return;
    const onFullscreenChange = () => {
      if (!document.fullscreenElement && attemptId) {
        void report(EV.FullscreenExit, SEV.High);
        void document.documentElement.requestFullscreen().catch(() => {});
      }
    };
    const onCopy = () => void report(EV.Copy, SEV.Medium);
    const onPaste = () => void report(EV.Paste, SEV.Medium);
    const onBlur = () => void report(EV.TabBlur, SEV.Medium);
    document.addEventListener('fullscreenchange', onFullscreenChange);
    document.addEventListener('copy', onCopy);
    document.addEventListener('paste', onPaste);
    window.addEventListener('blur', onBlur);
    return () => {
      document.removeEventListener('fullscreenchange', onFullscreenChange);
      document.removeEventListener('copy', onCopy);
      document.removeEventListener('paste', onPaste);
      window.removeEventListener('blur', onBlur);
    };
  }, [exam, report]);

  useEffect(() => () => streamRef.current?.getTracks().forEach(t => t.stop()), []);

  async function login() {
    setError('');
    try {
      const r = await fetch(`${API}/health`);
      if (!r.ok) throw new Error('API unreachable');
      localStorage.setItem('exam-token', secret);
      setToken(secret);
    } catch (e) { setError(String(e)); }
  }

  async function loadExam() {
    setError('');
    try {
      const r = await fetch(`${API}/exams/${examId}`, { headers: headers() });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const e = await r.json() as Exam;
      setExam(e);
    } catch (e) { setError(String(e)); }
  }

  async function begin() {
    setError('');
    try {
      const candidateId = crypto.randomUUID();
      const r = await fetch(`${API}/attempts?examId=${examId}`, {
        method: 'POST', headers: headers(),
        body: JSON.stringify({ candidateId, candidateName: 'Candidate ' + candidateId.slice(0, 6) }),
      });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const data = await r.json() as { attemptId: string };
      setAttemptId(data.attemptId);

      if (exam?.requireCamera || exam?.requireMicrophone) {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: exam.requireCamera, audio: exam.requireMicrophone,
        });
        streamRef.current = stream;
        if (videoRef.current) videoRef.current.srcObject = stream;
        stream.getVideoTracks().forEach(track => {
          track.addEventListener('ended', () => void report(EV.CameraBlocked, SEV.Critical));
        });
        stream.getAudioTracks().forEach(track => {
          track.addEventListener('ended', () => void report(EV.MicBlocked, SEV.Critical));
        });
      }
      await document.documentElement.requestFullscreen().catch(() => void report(EV.FullscreenExit, SEV.High));
    } catch (e) { setError(String(e)); }
  }

  async function save(questionId: string, value: string) {
    setAnswers(a => ({ ...a, [questionId]: value }));
    if (!attemptId) return;
    await fetch(`${API}/attempts/${attemptId}/answers`, {
      method: 'POST', headers: headers(),
      body: JSON.stringify({ questionId, responseJson: JSON.stringify({ value }) }),
    }).catch(() => void report(EV.Disconnected, SEV.Medium));
  }

  async function submit() {
    if (!attemptId) return;
    await fetch(`${API}/attempts/${attemptId}/submit`, { method: 'POST', headers: headers() });
    await fetch(`${API}/reports/generate?attemptId=${attemptId}`, { method: 'POST', headers: headers() });
    await loadReports();
    streamRef.current?.getTracks().forEach(t => t.stop());
    setAttemptId('');
  }

  async function loadReports() {
    const r = await fetch(`${API}/reports?examId=${examId}`, { headers: headers() });
    if (r.ok) setReports(await r.json() as Report[]);
  }

  return (
    <main>
      <h1>Proctored Exams</h1>
      <p className="muted">Standalone microservice — JWT from the main platform is accepted here.</p>

      {!token ? (
        <section className="card">
          <h2>Paste your access token</h2>
          <input value={secret} onChange={e => setSecret(e.target.value)} placeholder="eyJhbGci..." />
          <button onClick={login}>Continue</button>
        </section>
      ) : (
        <>
          <section className="card">
            <h2>Exam</h2>
            <input value={examId} onChange={e => setExamId(e.target.value)} placeholder="Exam ID" />
            <button onClick={loadExam}>Load</button>
            <button onClick={() => void loadReports()}>Reports</button>
          </section>

          {exam && (
            <section className="card">
              <h2>{exam.title}</h2>
              <p>{exam.instructions}</p>
              <p className="muted">{exam.durationMinutes} min · {exam.questions.length} questions · camera {exam.requireCamera ? 'on' : 'off'}</p>
              <video ref={videoRef} autoPlay muted style={{ width: 240, borderRadius: 8 }} />
              {!attemptId ? <button onClick={begin}>Start exam</button> : <button onClick={submit}>Submit</button>}
              <p className="warn">Violations recorded: {violations}</p>
              {exam.questions.map((q, i) => (
                <div key={q.id} className="question">
                  <strong>{i + 1}. {q.text}</strong>
                  {(() => {
                    try {
                      const options = JSON.parse(q.optionsJson ?? '[]') as { id: string; text: string }[];
                      if (!options.length) return <input onChange={e => void save(q.id, e.target.value)} placeholder="Your answer" />;
                      return options.map(o => (
                        <label key={o.id}><input type="radio" name={q.id} onChange={() => void save(q.id, o.id)} /> {o.text}</label>
                      ));
                    } catch { return null; }
                  })()}
                </div>
              ))}
            </section>
          )}

          {reports.length > 0 && (
            <section className="card">
              <h2>Candidate reports (AI-assisted)</h2>
              {reports.map(r => (
                <div key={r.id} className="report">
                  <strong>{r.candidateName}</strong> — score {r.totalScore?.toFixed(1) ?? 'pending'}%,
                  integrity {r.integrityScore.toFixed(0)}/100, violations {r.violationCount},
                  decision {['Pending','Approved','Flagged','Rejected'][r.decision]}
                </div>
              ))}
            </section>
          )}
        </>
      )}
      {error && <p className="error">{error}</p>}
    </main>
  );
}
