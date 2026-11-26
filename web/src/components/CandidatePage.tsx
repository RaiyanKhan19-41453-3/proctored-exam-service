import { useState } from 'react';
import { Stack, Title, TextInput, Button, Paper, Text, Radio, Textarea, Group, Badge, Alert, Progress } from '@mantine/core';
import { api, asArray, getToken } from '../api';

const typeNames=['Single choice','Multiple choice','True / false','Short answer','Essay','Numeric','Coding'];
export function CandidatePage(){
  const [token,setToken]=useState(getToken()),[examId,setExamId]=useState(''),[attemptToken,setAttemptToken]=useState(''),[assignmentId,setAssignmentId]=useState('');
  const [exam,setExam]=useState<any>(null),[attempt,setAttempt]=useState(''),[questions,setQuestions]=useState<any[]>([]),[answers,setAnswers]=useState<Record<string,string>>({});
  const [result,setResult]=useState<any>(null),[err,setErr]=useState(''),[notice,setNotice]=useState(''),[reportId,setReportId]=useState('');
  const [evidence,setEvidence]=useState<any[]>([]),[snapshotUrl,setSnapshotUrl]=useState('');
  const load=async()=>{setErr('');try{setExam(await api(`/api/exams/${examId}/public`));}catch(e:any){setErr(e.message);}};
  const begin=async()=>{setErr('');try{
    if(assignmentId) await api(`/api/candidate/consent?assignmentId=${assignmentId}&accepted=true`,{method:'POST'});
    const r:any=await api(`/api/attempts?examId=${examId}`,{body:{token:attemptToken}});
    setAttempt(r.attemptId); setQuestions(await api(`/api/attempts/${r.attemptId}/questions`));
  }catch(e:any){setErr(e.message);}};
  const save=async(qid:string,v:string)=>{setAnswers(s=>({...s,[qid]:v}));try{await api(`/api/attempts/${attempt}/answers`,{body:{questionId:qid,responseJson:JSON.stringify({value:v})}});}catch(e:any){setErr(e.message);}};
  const signal=async()=>{setErr('');setNotice('');try{
    await api(`/api/attempts/${attempt}/events`,{body:{type:2,severity:1,detailJson:'{"source":"candidate-ui"}',snapshotKey:null}});
    setNotice('Proctor event recorded');
  }catch(e:any){setErr(e.message);}};
  const verify=async()=>{setErr('');setNotice('');try{
    if(!assignmentId)throw new Error('Enter the assignment ID first');
    const r:any=await api(`/api/candidate/verify?assignmentId=${assignmentId}`,{body:btoa('demo-selfie-frame')});
    setNotice(`Identity verified: ${JSON.stringify(r)}`);
  }catch(e:any){setErr(e.message);}};
  const requestSnapshot=async()=>{setErr('');setNotice('');try{
    const r:any=await api(`/api/attempts/${attempt}/snapshots/upload-url`,{body:{fileName:`snapshot-${Date.now()}.png`,contentType:'image/png',sizeBytes:2048}});
    setSnapshotUrl(r.url); setNotice('Snapshot upload URL issued');
  }catch(e:any){setErr(e.message);}};
  const storeEvidence=async()=>{setErr('');setNotice('');try{
    const r:any=await api(`/api/evidence/attempts/${attempt}`,{body:{kind:0,contentType:'image/png',sizeBytes:24,checksum:null,detailJson:'{"source":"candidate-ui"}',dataBase64:btoa('demo-evidence-frame')}});
    setNotice(`Evidence stored: ${r.evidenceId?.slice(0,8)}…`);
    setEvidence(asArray(await api(`/api/evidence/attempts/${attempt}`)));
  }catch(e:any){setErr(e.message);}};
  const submit=async()=>{setErr('');
    try{
      await api(`/api/attempts/${attempt}/submit`,{method:'POST'});
      try{const g:any=await api(`/api/attempts/${attempt}/report`,{method:'POST'});setReportId(g.reportId);}catch{/* report may already exist */}
      try{setResult(await api(`/api/attempts/${attempt}/result`) ?? {totalScore:null,integrityScore:0});}
      catch{setResult({totalScore:null,integrityScore:0});}
    }catch(e:any){setErr(e.message);}
    setAttempt('');
  };
  return <Stack gap="md"><Title order={2}>Take an exam</Title><Text c="dimmed">Normal browser · identity binding via attempt token</Text>
    {err&&<Alert color="red">{err}</Alert>}
    {notice&&<Alert color="green">{notice}</Alert>}
    <Paper withBorder p="md"><Stack gap="sm">
      <TextInput label="Access token" value={token} onChange={e=>setToken(e.currentTarget.value)} placeholder="Paste JWT from the main platform"/>
      <Button size="xs" onClick={()=>setToken(getToken())}>Use saved token</Button>
      <Group grow><TextInput label="Exam ID" value={examId} onChange={e=>setExamId(e.currentTarget.value)}/><TextInput label="Attempt token" value={attemptToken} onChange={e=>setAttemptToken(e.currentTarget.value)}/><TextInput label="Assignment ID" value={assignmentId} onChange={e=>setAssignmentId(e.currentTarget.value)}/></Group>
      <Group><Button onClick={load}>Load exam</Button>{exam&&!attempt&&<Button onClick={begin}>Start attempt</Button>}{attempt&&<Button color="green" onClick={submit}>Submit exam</Button>}</Group>
    </Stack></Paper>
    {exam&&<Paper withBorder p="md"><Group justify="space-between"><Title order={3}>{exam.title}</Title><Badge color="teal">{exam.durationMinutes} min</Badge></Group><Text c="dimmed" mb="md">{exam.instructions}</Text>
      {questions.map((q,i)=><Paper key={q.questionId ?? q.id} withBorder p="sm" mb="sm">
        <Text fw={600}>{i+1}. {q.text}</Text><Text size="xs" c="dimmed">{q.marks} marks · {typeNames[q.type] ?? q.type}</Text>
        {q.type===2 ? <Radio.Group value={answers[q.questionId ?? q.id]??''} onChange={v=>save(q.questionId ?? q.id,v)} mt="sm"><Radio value="true" label="True"/><Radio value="false" label="False"/></Radio.Group>
        :q.type===0||q.type===1 ? (()=>{try{const opts=JSON.parse(q.optionsJson??'[]');return <Radio.Group value={answers[q.questionId ?? q.id]??''} onChange={v=>save(q.questionId ?? q.id,v)} mt="sm">{opts.map((o:any)=><Radio key={o.id} value={o.id} label={o.text}/>)}</Radio.Group>;}catch{return null;}})()
        :<Textarea mt="sm" placeholder="Your answer" value={answers[q.questionId ?? q.id]??''} onChange={e=>save(q.questionId ?? q.id,e.currentTarget.value)} autosize minRows={3}/>}
      </Paper>)}
    </Paper>}
    {attempt&&<Paper withBorder p="md"><Group justify="space-between"><Title order={4}>Proctoring signals & evidence</Title><Badge color="teal" variant="light">Attempt {attempt.slice(0,8)}…</Badge></Group>
      <Text size="sm" c="dimmed" mb="sm">Expose the identity, event, snapshot and evidence flows used by the proctor service.</Text>
      <Group mb="sm"><Button size="xs" variant="light" onClick={verify}>Verify identity</Button>
        <Button size="xs" variant="light" onClick={signal}>Record tab-blur event</Button>
        <Button size="xs" variant="light" onClick={requestSnapshot}>Snapshot upload URL</Button>
        <Button size="xs" variant="light" onClick={storeEvidence}>Store evidence frame</Button></Group>
      {snapshotUrl&&<Text size="xs" style={{wordBreak:'break-all'}}>PUT {snapshotUrl}</Text>}
      {evidence.length>0&&<Text size="sm" mt="sm">{evidence.length} evidence item(s) · kinds: {evidence.map(e=>e.kind).join(', ')}</Text>}
    </Paper>}
    {result&&<Paper withBorder p="md"><Title order={4} mb="sm">Result</Title>
      <Text fw={700} mb="xs">{result.totalScore??0}%</Text><Progress value={result.totalScore??0} mb="sm"/>
      <Text>Integrity: {result.integrityScore}/100 · Report: {reportId.slice(0,8)}…</Text></Paper>}
  </Stack>;
}
