// Concurrent candidate simulation. Set RATE_LIMIT_PER_MINUTE high before running.
import crypto from 'node:crypto';

const BASE = process.env.EXAM_API ?? 'http://localhost:18090';
const KEY = process.env.JWT_KEY ?? 'dev-only-signing-key-change-this-in-production-1234';
const USERS = Number(process.env.USERS ?? 50);
const TENANT = '22222222-2222-2222-2222-222222222222';

const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url');
function token(sub, role, expSeconds = 3600) {
  const h = b64({ alg: 'HS256', typ: 'JWT' });
  const p = b64({ sub, tenant_id: TENANT, email: `${sub}@load.test`, role: [role],
    exp: Math.floor(Date.now() / 1000) + expSeconds, iss: 'coaching-platform', aud: 'coaching-web' });
  return `${h}.${p}.${crypto.createHmac('sha256', KEY).update(`${h}.${p}`).digest('base64url')}`;
}
const examiner = token('11111111-1111-1111-1111-111111111111', 'Admin');
const call = async (path, body, tok, method) => {
  const t0 = performance.now();
  const r = await fetch(`${BASE}${path}`, {
    method: method ?? (body ? 'POST' : 'GET'),
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${tok}` },
    body: body ? JSON.stringify(body) : undefined,
  });
  const ms = performance.now() - t0;
  const text = await r.text();
  return { ok: r.ok, status: r.status, ms, text };
};

const exam = await call('/api/exams', {
  title: `Load test ${Date.now()}`, durationMinutes: 30, totalMarks: 1,
  requireCamera: false, requireMicrophone: false, lockTab: false,
  disableCopyPaste: false, aiEvaluationEnabled: false,
  requireIdentityVerification: false,
}, examiner);
if (!exam.ok) throw new Error(`create exam: ${exam.status} ${exam.text}`);
const examId = JSON.parse(exam.text).id;
await call(`/api/exams/${examId}/questions`, {
  type: 0, text: '1+1?', optionsJson: '[{"id":"a","text":"2"}]',
  correctAnswerJson: '{"value":"a"}', marks: 1, order: 1,
}, examiner);
await call(`/api/exams/${examId}/publish`, null, examiner, 'POST');

console.log(`Simulating ${USERS} candidates...`);
const candidates = Array.from({ length: USERS }, () => crypto.randomUUID());
let created = 0;
for (let i = 0; i < candidates.length; i += 20) {
  const slice = candidates.slice(i, i + 20);
  const results = await Promise.all(slice.map(async cid => {
    const a = await call('/api/candidates/assign', {
      examId, candidateId: cid, candidateName: `Candidate ${cid.slice(0, 6)}`,
      candidateEmail: `${cid}@load.test`, identityPhotoKey: null,
    }, examiner);
    if (!a.ok) return { step: 'assign', status: a.status };
    const { assignmentId, attemptToken } = JSON.parse(a.text);
    const tok = token(cid, 'Student');
    const consent = await call(`/api/candidate/consent?assignmentId=${assignmentId}&accepted=true`, null, tok, 'POST');
    if (!consent.ok) return { step: 'consent', status: consent.status };
    const start = await call(`/api/attempts?examId=${examId}`, { token: attemptToken }, tok);
    if (!start.ok) return { step: 'start', status: start.status };
    const attemptId = JSON.parse(start.text).attemptId;
    const answer = await call(`/api/attempts/${attemptId}/answers`, {
      questionId: (await call(`/api/attempts/${attemptId}/questions`, null, tok).then(r => JSON.parse(r.text)[0].questionId)),
      responseJson: '{"value":"a"}',
    }, tok);
    if (!answer.ok) return { step: 'answer', status: answer.status };
    const submit = await call(`/api/attempts/${attemptId}/submit`, null, tok, 'POST');
    if (!submit.ok) return { step: 'submit', status: submit.status };
    const report = await call(`/api/attempts/${attemptId}/report`, null, tok, 'POST');
    if (!report.ok) return { step: 'report', status: report.status };
    return { step: 'done', status: 200 };
  }));
  created += results.filter(r => r.step === 'done').length;
  const failures = results.filter(r => r.step !== 'done');
  if (failures.length) console.log('Failures:', JSON.stringify(failures.slice(0, 5)));
}
await call(`/api/reports/exams/${examId}/publish`, null, examiner, 'POST');

const reports = await call(`/api/reports?examId=${examId}`, null, examiner);
const count = reports.ok ? JSON.parse(reports.text).length : -1;
console.log(JSON.stringify({
  users: USERS, completed: created, reports: count,
  successRate: `${Math.round(created / USERS * 100)}%`,
}, null, 2));
if (created !== USERS || count !== USERS) process.exit(1);
