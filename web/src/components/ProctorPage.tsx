import { useState } from 'react';
import { Stack, Title, TextInput, Button, Paper, Table, Badge, Group, Text, Alert, Image, Accordion } from '@mantine/core';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { api, getToken, asArray } from '../api';

const flagKinds=['Answer similarity','Shared device','Shared IP','Geo anomaly','Kiosk required'];
export function ProctorPage(){
  const [examId,setExamId]=useState(''),[flags,setFlags]=useState<any[]>([]),[events,setEvents]=useState<any[]>([]),[evidence,setEvidence]=useState<any[]>([]);
  const [live,setLive]=useState<any[]>([]),[status,setStatus]=useState('idle'),[err,setErr]=useState(''),[notice,setNotice]=useState('');
  const [ctxAttempt,setCtxAttempt]=useState(''),[ip,setIp]=useState(''),[ua,setUa]=useState(''),[fp,setFp]=useState(''),[country,setCountry]=useState(''),[city,setCity]=useState('');
  const loadFlags=()=>api(`/api/proctor/exams/${examId}/flags`).then(r=>setFlags(asArray(r))).catch(e=>setErr(e.message));
  const loadEvents=()=>api(`/api/proctor/exams/${examId}/events`).then(r=>setEvents(asArray(r))).catch(e=>setErr(e.message));
  const detect=()=>api(`/api/proctor/exams/${examId}/detect`,{method:'POST'}).then(()=>loadFlags()).catch(e=>setErr(e.message));
  const loadEvidence=async(attempt:string)=>{try{const rows:any[]=await api(`/api/evidence/attempts/${attempt}`);const withUrls=await Promise.all(rows.slice(0,24).map(async r=>{const u:any=await api(`/api/evidence/${r.id}/url`);return {...r,url:u.url};}));setEvidence(withUrls);}catch(e:any){setErr(e.message);}};
  const recordContext=async()=>{setErr('');setNotice('');try{
    await api(`/api/proctor/attempts/${ctxAttempt}/context`,{body:{ipAddress:ip||null,userAgent:ua||null,deviceFingerprint:fp||null,countryCode:country||null,city:city||null}});
    setNotice('Attempt context recorded');
  }catch(e:any){setErr(e.message);}};
  const connect=()=>{const c=new HubConnectionBuilder().withUrl(`/hubs/proctor?examId=${examId}`,{accessTokenFactory:()=>getToken()}).withAutomaticReconnect().build();c.on('proctor-event',e=>setLive(p=>[e,...p].slice(0,40)));c.on('anomaly-flag',()=>loadFlags());c.start().then(()=>setStatus('live')).catch(()=>setStatus('offline'));};
  return <Stack gap="md"><Group justify="space-between"><Title order={2}>Live proctor</Title><Badge color={status==='live'?'green':'gray'}>{status}</Badge></Group>
    {err&&<Alert color="red">{err}</Alert>}
    {notice&&<Alert color="green">{notice}</Alert>}
    <Paper withBorder p="sm"><Group><TextInput label="Exam ID" value={examId} onChange={e=>setExamId(e.currentTarget.value)}/><Button onClick={()=>{loadFlags();loadEvents();connect();}}>Watch</Button><Button variant="light" onClick={detect}>Detect anomalies</Button></Group></Paper>
    <Paper withBorder p="sm"><Title order={4} mb="xs">Live events</Title>{live.length===0?<Text c="dimmed" size="sm">Waiting…</Text>:live.map((e:any,i)=><Text key={i} size="sm">{e.severity} · {e.type} · {e.attemptId?.slice(0,8)}</Text>)}</Paper>
    <Paper withBorder><Group justify="space-between" p="sm"><Title order={4}>Anomaly flags</Title><Text size="xs" c="dimmed">Showing {Math.min(flags.length, 8)} of {flags.length}</Text></Group><Table><Table.Thead><Table.Tr><Table.Th>Kind</Table.Th><Table.Th>Score</Table.Th><Table.Th>Reviewed</Table.Th><Table.Th/></Table.Tr></Table.Thead><Table.Tbody>{flags.slice(0,8).map(f=><Table.Tr key={f.id}><Table.Td>{flagKinds[f.kind] ?? f.kind}</Table.Td><Table.Td>{f.score}</Table.Td><Table.Td><Badge color={f.reviewed?'green':'gray'}>{f.reviewed?'Yes':'No'}</Badge></Table.Td><Table.Td><Button size="xs" variant="light" onClick={()=>api(`/api/proctor/flags/${f.id}/review?reviewed=true`,{method:'POST'}).then(loadFlags)}>Mark reviewed</Button></Table.Td></Table.Tr>)}</Table.Tbody></Table></Paper>
    <Paper withBorder><Title order={4} p="sm">Event history</Title><Table><Table.Thead><Table.Tr><Table.Th>Candidate</Table.Th><Table.Th>Event</Table.Th><Table.Th>Severity</Table.Th><Table.Th>Time</Table.Th><Table.Th/></Table.Tr></Table.Thead><Table.Tbody>{events.slice(0,60).map((e:any,i)=><Table.Tr key={i}><Table.Td>{e.candidateName}</Table.Td><Table.Td>{e.type}</Table.Td><Table.Td>{e.severity}</Table.Td><Table.Td>{new Date(e.at).toLocaleString()}</Table.Td><Table.Td><Button size="xs" variant="subtle" onClick={()=>loadEvidence(e.attemptId)}>Evidence</Button></Table.Td></Table.Tr>)}</Table.Tbody></Table></Paper>
    <Paper withBorder p="sm"><Title order={4} mb="xs">Attempt context</Title>
      <Group align="flex-end" mb="sm" wrap="wrap">
        <TextInput label="Attempt ID" value={ctxAttempt} onChange={e=>setCtxAttempt(e.currentTarget.value)}/>
        <TextInput label="IP" value={ip} onChange={e=>setIp(e.currentTarget.value)}/>
        <TextInput label="User agent" value={ua} onChange={e=>setUa(e.currentTarget.value)}/>
        <TextInput label="Device fingerprint" value={fp} onChange={e=>setFp(e.currentTarget.value)}/>
        <TextInput label="Country" value={country} onChange={e=>setCountry(e.currentTarget.value)}/>
        <TextInput label="City" value={city} onChange={e=>setCity(e.currentTarget.value)}/>
        <Button onClick={recordContext} disabled={!ctxAttempt}>Record</Button>
      </Group>
    </Paper>
    {evidence.length>0&&<Accordion><Accordion.Item value="evidence"><Accordion.Control>Evidence frames ({evidence.length})</Accordion.Control><Accordion.Panel><Group wrap="wrap">{evidence.filter(e=>e.kind===0).map(e=><Image key={e.id} src={e.url} w={160} fit="contain"/>)}{evidence.filter(e=>e.kind!==0).map(e=><a key={e.id} href={e.url} target="_blank" rel="noreferrer">Recording #{e.kind}</a>)}</Group></Accordion.Panel></Accordion.Item></Accordion>}
  </Stack>;
}
