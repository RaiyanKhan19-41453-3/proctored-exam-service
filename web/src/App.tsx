import { useCallback, useEffect, useRef, useState } from 'react';
import Proctor from './Proctor';
import { enqueue, flush, pendingCount } from './offline';

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
  const [attemptToken, setAttemptToken] = useState('');
  const [assignmentId, setAssignmentId] = useState('');
  const [candidateId] = useState(() => localStorage.getItem('exam-candidate') ?? crypto.randomUUID());
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [reports, setReports] = useState<Report[]>([]);
  const [error, setError] = useState('');
  const [proctorOn, setProctorOn] = useState(false);
  const [queued, setQueued] = useState(pendingCount());
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

  useEffect(() => {
    if (!token) return;
    const retry = () => void flush(token).then(n => { if (n) setQueued(pendingCount()); });
    window.addEventListener('online', retry);
    retry();
    return () => window.removeEventListener('online', retry);
  }, [token]);

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
      localStorage.setItem('exam-candidate', candidateId);
      await fetch(`${API}/candidates/consent?assignmentId=${assignmentId}&accepted=true`, { method: 'POST', headers: headers() });
      const r = await fetch(`${API}/attempts?examId=${examId}`, {
        method: 'POST', headers: headers(), body: JSON.stringify({ token: attemptToken }),
      });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      const data = await r.json() as { attemptId: string };
      setAttemptId(data.attemptId);

      let stream: MediaStream | null = null;
      if (exam?.requireCamera || exam?.requireMicrophone) {
        stream = await navigator.mediaDevices.getUserMedia({
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
      startEvidenceCapture(data.attemptId, stream ?? new MediaStream());
    } catch (e) { setError(String(e)); }
  }

  async function save(questionId: string, value: string) {
    setAnswers(a => ({ ...a, [questionId]: value }));
    if (!attemptId) return;
    const body = { questionId, responseJson: JSON.stringify({ value }) };
    try {
      const r = await fetch(`${API}/attempts/${attemptId}/answers`, {
        method: 'POST', headers: headers(), body: JSON.stringify(body),
      });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
    } catch {
      enqueue(`${API}/attempts/${attemptId}/answers`, body);
      setQueued(pendingCount());
      void report(EV.Disconnected, SEV.Medium);
    }
  }

  function toBase64(blob: Blob): Promise<string> {
  return new Promise(resolve => {
    const reader = new FileReader();
    reader.onload = () => resolve((reader.result as string).split(',')[1]);
    reader.readAsDataURL(blob);
  });
}

  function startEvidenceCapture(id: string, stream: MediaStream) {
    const video = videoRef.current;
    const canvas = document.createElement('canvas');
    const captureFrame = async () => {
      if (!video || !video.videoWidth) return;
      canvas.width = 320; canvas.height = 240;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
      canvas.toBlob(async blob => {
        if (!blob) return;
        const base64 = await toBase64(blob);
        try {
          await fetch(`${API}/evidence/attempts/${id}`, {
            method: 'POST', headers: headers(),
            body: JSON.stringify({ kind: 0, contentType: 'image/jpeg', sizeBytes: blob.size, detailJson: null, checksum: null, dataBase64: base64 }),
          }).catch(() => void report(EV.Disconnected, SEV.Medium));
        } catch { /* ignore */ }
      }, 'image/jpeg', 0.6);
    };
    const timer = window.setInterval(() => void captureFrame(), 20000);
    const audioTrack = stream.getAudioTracks()[0];
    if (audioTrack) {
      try {
        const recorder = new MediaRecorder(new MediaStream([audioTrack]), { mimeType: 'audio/webm' });
        const chunks: Blob[] = [];
        recorder.ondataavailable = e => { if (e.data.size) chunks.push(e.data); };
        recorder.onstop = async () => {
          if (!chunks.length) return;
          const blob = new Blob(chunks, { type: 'audio/webm' });
          const base64 = await toBase64(blob);
          try {
            await fetch(`${API}/evidence/attempts/${id}`, {
              method: 'POST', headers: headers(),
              body: JSON.stringify({ kind: 2, contentType: 'audio/webm', sizeBytes: blob.size, detailJson: null, checksum: null, dataBase64: base64 }),
            }).catch(() => {});
          } catch { /* ignore */ }
          chunks.length = 0;
        };
        const audioTimer = window.setInterval(() => recorder.state === 'inactive' && recorder.start(), 15000);
        const stopTimer = window.setInterval(() => recorder.state === 'recording' && recorder.stop(), 28000);
        audioTrack.addEventListener('ended', () => {
          window.clearInterval(audioTimer); window.clearInterval(stopTimer);
          if (recorder.state === 'recording') recorder.stop();
        });
      } catch { /* MediaRecorder unsupported */ }
    }
    void captureFrame();
    stream.getTracks().forEach(t => t.addEventListener('ended', () => window.clearInterval(timer)));
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
            <input value={attemptToken} onChange={e => setAttemptToken(e.target.value)} placeholder="Attempt token (from examiner)" />
            <input value={assignmentId} onChange={e => setAssignmentId(e.target.value)} placeholder="Assignment ID" />
            <button onClick={loadExam}>Load</button>
            <button onClick={() => void loadReports()}>Reports</button>
            <button onClick={() => setProctorOn(v => !v)}>{proctorOn ? 'Hide live proctor' : 'Open live proctor'}</button>
          </section>

          {proctorOn && <Proctor token={token} examId={examId} />}

          {exam && (
            <section className="card">
              <h2>{exam.title}</h2>
              <p>{exam.instructions}</p>
              <p className="muted">{exam.durationMinutes} min · {exam.questions.length} questions · camera {exam.requireCamera ? 'on' : 'off'}</p>
              <video ref={videoRef} autoPlay muted style={{ width: 240, borderRadius: 8 }} />
              {!attemptId ? <button onClick={begin}>Start exam</button> : <button onClick={submit}>Submit</button>}
              <p className="warn">Violations recorded: {violations} · Offline saves queued: {queued}</p>
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
