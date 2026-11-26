import { useEffect, useState } from 'react';
import { Stack, Title, Table, Paper, Button, Group, Badge, Alert, Text, Modal, Textarea, TextInput } from '@mantine/core';
import { api, asArray } from '../api';

export function ReportsPage(){
  const [reports,setReports]=useState<any[]>([]),[detail,setDetail]=useState<any>(null),[err,setErr]=useState(''),[notice,setNotice]=useState(''),[attemptId,setAttemptId]=useState('');
  const load=()=>{api('/api/reports').then(r=>setReports(asArray(r))).catch(e=>setErr(e.message));};
  useEffect(()=>{load();},[]);
  const open=async(id:string)=>{try{setDetail(await api(`/api/reports/${id}`));}catch(e:any){setErr(e.message);}};
  const generate=async()=>{setErr('');setNotice('');try{
    const r:any=await api(`/api/reports/generate?attemptId=${attemptId}`,{method:'POST'});
    setNotice(`Report generated: ${r.reportId ?? JSON.stringify(r)}`); setAttemptId(''); load();
  }catch(e:any){setErr(e.message);}};
  const review=async(id:string,decision:string)=>{try{await api(`/api/reports/${id}/review`,{body:{decision,notes:''}});setDetail(null);load();}catch(e:any){setErr(e.message);}};
  return <Stack gap="md"><Title order={2}>Candidate reports</Title><Text c="dimmed">AI-assisted scoring, integrity flags and review decisions</Text>
    {err&&<Alert color="red">{err}</Alert>}
    {notice&&<Alert color="green">{notice}</Alert>}
    <Paper withBorder p="sm"><Group align="flex-end"><TextInput label="Attempt ID to grade" value={attemptId} onChange={e=>setAttemptId(e.currentTarget.value)} placeholder="GUID"/><Button onClick={generate} disabled={!attemptId}>Generate report</Button></Group></Paper>
    <Paper withBorder><Table><Table.Thead><Table.Tr><Table.Th>Candidate</Table.Th><Table.Th>Score</Table.Th><Table.Th>Integrity</Table.Th><Table.Th>Decision</Table.Th><Table.Th>Violations</Table.Th><Table.Th/></Table.Tr></Table.Thead>
      <Table.Tbody>{reports.map(r=><Table.Tr key={r.id}><Table.Td>{r.candidateName}</Table.Td><Table.Td>{r.totalScore??'—'}%</Table.Td><Table.Td>{r.integrityScore}</Table.Td><Table.Td><Badge color={r.decision===1?'green':r.decision===2?'yellow':'gray'}>{['Pending','Approved','Flagged','Rejected'][r.decision]}</Badge></Table.Td><Table.Td>{r.violationCount}</Table.Td><Table.Td><Button size="xs" variant="light" onClick={()=>open(r.id)}>Review</Button></Table.Td></Table.Tr>)}</Table.Tbody></Table></Paper>
    <Modal opened={!!detail} onClose={()=>setDetail(null)} title="Report detail" size="lg">{detail&&<Stack gap="sm">
      <Text>Score: {detail.totalScore}% · Objective: {detail.objectiveScore}% · Subjective: {detail.subjectiveScore}%</Text>
      <Text>Integrity: {detail.integrityScore}/100</Text>
      <Textarea label="AI summary" value={detail.aiSummary??''} readOnly autosize minRows={3}/>
      <Group><Button color="green" onClick={()=>review(detail.id,'Approved')}>Approve</Button><Button color="yellow" onClick={()=>review(detail.id,'Flagged')}>Flag</Button><Button color="red" onClick={()=>review(detail.id,'Rejected')}>Reject</Button></Group>
      <Table><Table.Thead><Table.Tr><Table.Th>Event</Table.Th><Table.Th>Severity</Table.Th><Table.Th>Time</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{detail.events.map((e:any,i:number)=><Table.Tr key={i}><Table.Td>{e.type}</Table.Td><Table.Td>{e.severity}</Table.Td><Table.Td>{new Date(e.at).toLocaleString()}</Table.Td></Table.Tr>)}</Table.Tbody></Table>
    </Stack>}</Modal>
  </Stack>;
}
