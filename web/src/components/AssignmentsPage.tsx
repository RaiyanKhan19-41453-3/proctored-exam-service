import { useState } from 'react';
import { Stack, Title, Paper, TextInput, Button, Group, Alert, Text, Badge } from '@mantine/core';
import { api } from '../api';
export function AssignmentsPage(){
  const [examId,setExamId]=useState(''),[candidateId,setCandidateId]=useState(''),[name,setName]=useState('');
  const [result,setResult]=useState<any>(null),[err,setErr]=useState('');
  const assign=async()=>{setErr('');try{setResult(await api('/api/candidates/assign',{body:{examId,candidateId,candidateName:name,candidateEmail:`${candidateId}@candidate.local`}}));}catch(e:any){setErr(e.message);}};
  const consent=async()=>{if(!result)return;try{await api(`/api/candidate/consent?assignmentId=${result.assignmentId}&accepted=true`,{method:'POST'});setErr('Consent accepted');}catch(e:any){setErr(e.message);}};
  return <Stack gap="md"><Title order={2}>Assignments</Title><Text c="dimmed">Issue a single-use attempt token to a candidate</Text>{err&&<Alert color={err==='Consent accepted'?'green':'red'}>{err}</Alert>}
    <Paper withBorder p="md"><Stack gap="sm"><TextInput label="Exam ID" value={examId} onChange={e=>setExamId(e.currentTarget.value)}/><TextInput label="Candidate ID (GUID)" value={candidateId} onChange={e=>setCandidateId(e.currentTarget.value)}/><TextInput label="Candidate name" value={name} onChange={e=>setName(e.currentTarget.value)}/><Button onClick={assign} disabled={!examId||!candidateId}>Assign</Button></Stack></Paper>
    {result&&<Paper withBorder p="md"><Title order={4} mb="sm">Attempt token</Title><Badge color="teal">Single use · expires {new Date(result.expiresAt).toLocaleString()}</Badge><Text mt="sm" style={{fontFamily:'monospace',wordBreak:'break-all'}}>{result.attemptToken}</Text><Group mt="sm"><Button onClick={consent}>Accept consent (candidate token)</Button><Button variant="light" onClick={()=>navigator.clipboard.writeText(result.attemptToken)}>Copy token</Button></Group></Paper>}
  </Stack>;
}
