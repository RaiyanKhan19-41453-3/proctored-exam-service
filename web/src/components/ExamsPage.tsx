import { useEffect, useState } from 'react';
import { Stack, Title, Button, Table, Paper, Modal, TextInput, NumberInput, Select, Checkbox, Group, Badge, Text, Alert, Textarea } from '@mantine/core';
import { IconPlus } from '@tabler/icons-react';
import { api, asArray } from '../api';

export function ExamsPage(){
  const [exams,setExams]=useState<any[]>([]),[opened,setOpened]=useState(false),[sel,setSel]=useState<any>(null),[err,setErr]=useState('');
  const [f,setF]=useState<Record<string,any>>({title:'',durationMinutes:60,totalMarks:10,requireCamera:false,requireMicrophone:false,lockTab:true,disableCopyPaste:true,aiEvaluationEnabled:true,requireIdentityVerification:false});
  const [q,setQ]=useState<Record<string,any>>({type:'SingleChoice',text:'',marks:1,order:1});
  const [cand,setCand]=useState({candidateId:'',candidateName:''});
  const load=()=>{api('/api/exams').then(r=>setExams(asArray(r))).catch(e=>setErr(e.message));};
  useEffect(()=>{load();},[]);
  const create=async()=>{try{await api('/api/exams',{body:f});setOpened(false);load();}catch(e:any){setErr(e.message);}};
  const openDetail=async(id:string)=>{try{setSel(await api(`/api/exams/${id}`));}catch(e:any){setErr(e.message);}};
  const addQuestion=async()=>{if(!sel)return;try{await api(`/api/exams/${sel.id}/questions`,{body:q});await openDetail(sel.id);}catch(e:any){setErr(e.message);}};
  const publish=async()=>{if(!sel)return;try{await api(`/api/exams/${sel.id}/publish`,{method:'POST'});await openDetail(sel.id);load();}catch(e:any){setErr(e.message);}};
  const assign=async()=>{if(!sel)return;try{await api('/api/candidates/assign',{body:{...cand,examId:sel.id,candidateEmail:`${cand.candidateId}@candidate.local`}});await openDetail(sel.id);}catch(e:any){setErr(e.message);}};
  return <Stack gap="md"><Group justify="space-between"><div><Title order={2}>Exams</Title><Text c="dimmed">Create papers, questions and candidate assignments</Text></div><Button leftSection={<IconPlus size={16}/>} onClick={()=>setOpened(true)}>New exam</Button></Group>
    {err&&<Alert color="red">{err}</Alert>}
    <Paper withBorder><Table><Table.Thead><Table.Tr><Table.Th>Title</Table.Th><Table.Th>Status</Table.Th><Table.Th>Duration</Table.Th><Table.Th>Marks</Table.Th><Table.Th/></Table.Tr></Table.Thead>
      <Table.Tbody>{exams.map(e=><Table.Tr key={e.id}><Table.Td>{e.title}</Table.Td><Table.Td><Badge color={e.status===1?'green':'gray'}>{['Draft','Published','Ongoing','Completed'][e.status]??e.status}</Badge></Table.Td><Table.Td>{e.durationMinutes} min</Table.Td><Table.Td>{e.totalMarks}</Table.Td><Table.Td><Button size="xs" variant="light" onClick={()=>openDetail(e.id)}>Manage</Button></Table.Td></Table.Tr>)}</Table.Tbody></Table></Paper>
    <Modal opened={opened} onClose={()=>setOpened(false)} title="Create exam" size="lg"><Stack gap="sm">
      <TextInput label="Title" value={f.title} onChange={e=>setF({...f,title:e.currentTarget.value})}/>
      <NumberInput label="Duration (minutes)" value={f.durationMinutes} onChange={v=>setF({...f,durationMinutes:Number(v)})}/>
      <NumberInput label="Total marks" value={f.totalMarks} onChange={v=>setF({...f,totalMarks:Number(v)})}/>
      <Checkbox label="Require camera" checked={f.requireCamera} onChange={e=>setF({...f,requireCamera:e.currentTarget.checked})}/>
      <Checkbox label="Require microphone" checked={f.requireMicrophone} onChange={e=>setF({...f,requireMicrophone:e.currentTarget.checked})}/>
      <Checkbox label="Lock tab" checked={f.lockTab} onChange={e=>setF({...f,lockTab:e.currentTarget.checked})}/>
      <Checkbox label="Disable copy/paste" checked={f.disableCopyPaste} onChange={e=>setF({...f,disableCopyPaste:e.currentTarget.checked})}/>
      <Checkbox label="AI evaluation" checked={f.aiEvaluationEnabled} onChange={e=>setF({...f,aiEvaluationEnabled:e.currentTarget.checked})}/>
      <Checkbox label="Require identity verification" checked={f.requireIdentityVerification} onChange={e=>setF({...f,requireIdentityVerification:e.currentTarget.checked})}/>
      <Group justify="flex-end"><Button onClick={create}>Create</Button></Group>
    </Stack></Modal>
    <Modal opened={!!sel} onClose={()=>setSel(null)} title={sel?.title ?? ''} size="xl">
      {sel&&<Stack gap="md">
        <Group><Badge color={sel.status===1?'green':'gray'}>{sel.status===1?'Published':'Draft'}</Badge><Text c="dimmed">{sel.questions.length} questions</Text><Button size="xs" onClick={publish} disabled={sel.status===1}>Publish</Button></Group>
        <Paper withBorder p="sm"><Title order={4} mb="xs">Add question</Title><Group align="flex-end">
          <Select label="Type" data={[{value:'SingleChoice',label:'Single choice'},{value:'MultipleChoice',label:'Multiple choice'},{value:'ShortAnswer',label:'Short answer'},{value:'Essay',label:'Essay'}]} value={q.type} onChange={v=>setQ({...q,type:v??'SingleChoice'})}/>
          <TextInput label="Question" value={q.text} onChange={e=>setQ({...q,text:e.currentTarget.value})}/>
          <NumberInput label="Marks" value={q.marks} onChange={v=>setQ({...q,marks:Number(v)})}/>
          <Button onClick={addQuestion}>Add</Button>
        </Group>{q.type.startsWith('Single')||q.type.startsWith('Multiple')?<TextInput mt="sm" label='Options JSON' placeholder='[{"id":"a","text":"A"}]' value={q.optionsJson??''} onChange={e=>setQ({...q,optionsJson:e.currentTarget.value})}/>:null}
        {q.type.startsWith('Single')||q.type.startsWith('Multiple')?<TextInput mt="sm" label='Correct answer JSON' placeholder='{"value":"a"}' value={q.correctAnswerJson??''} onChange={e=>setQ({...q,correctAnswerJson:e.currentTarget.value})}/>:null}
        </Paper>
        <Paper withBorder><Table><Table.Thead><Table.Tr><Table.Th>#</Table.Th><Table.Th>Question</Table.Th><Table.Th>Type</Table.Th><Table.Th>Marks</Table.Th></Table.Tr></Table.Thead><Table.Tbody>{sel.questions.map((x:any,i:number)=><Table.Tr key={x.id}><Table.Td>{i+1}</Table.Td><Table.Td>{x.text}</Table.Td><Table.Td>{x.type}</Table.Td><Table.Td>{x.marks}</Table.Td></Table.Tr>)}</Table.Tbody></Table></Paper>
        <Paper withBorder p="sm"><Title order={4} mb="xs">Assign a candidate</Title><Group>
          <TextInput label="Candidate ID (GUID)" value={cand.candidateId} onChange={e=>setCand({...cand,candidateId:e.currentTarget.value})}/>
          <TextInput label="Name" value={cand.candidateName} onChange={e=>setCand({...cand,candidateName:e.currentTarget.value})}/>
          <Button onClick={assign} disabled={!cand.candidateId}>Assign</Button>
        </Group><Text size="xs" c="dimmed" mt="xs">The returned attempt token is shown in the API response — copy it into the candidate view.</Text></Paper>
      </Stack>}
    </Modal>
  </Stack>;
}
