import { useEffect, useState } from 'react';
import { Button, Group, Modal, Stack, Table, TextInput, Select, NumberInput, Textarea, Title, Paper, Badge, Text, Alert, Checkbox } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { IconPlus } from '@tabler/icons-react';
import { api, asArray } from '../api';

type Field={name:string;label:string;type?:'text'|'number'|'select'|'textarea'|'boolean';options?:{value:string;label:string}[];required?:boolean;default?:any};
export type Resource={key:string;label:string;path:string;fields:Field[];columns:string[];
  createPath?:string;noCreate?:boolean;actions?:{label:string;path:(id:string)=>string;method?:string;body?:Record<string,unknown>}[]};

export function ResourcePage({r}:{r:Resource}){
  const [rows,setRows]=useState<any[]>([]),[values,setValues]=useState<Record<string,any>>({}),[err,setErr]=useState(''),[busy,setBusy]=useState(false);
  const [opened,{open,close}]=useDisclosure(false);
  const load=()=>{setErr(''); api(r.path).then(x=>setRows(asArray(x))).catch(e=>setErr(e.message));};
  useEffect(()=>{load();},[r.key]);
  const submit=async()=>{setBusy(true);setErr('');try{
    const body:Record<string,any>={};
    for(const f of r.fields){let v=values[f.name]; if(v===''||v===undefined){if(f.default!==undefined)v=f.default;else continue;}
      if(f.type==='number')v=Number(v);
      if(f.type==='boolean')v=!!v;
      body[f.name]=v;}
    await api(r.createPath??r.path,{body});setValues({});close();load();
  }catch(e:any){setErr(e.message);}finally{setBusy(false);}};
  const run=async(a:any,id:string)=>{try{await api(a.path(id),{method:a.method??'POST',body:a.body});load();}catch(e:any){setErr(e.message);}};
  return <Stack gap="md"><Group justify="space-between"><div><Title order={2}>{r.label}</Title><Text c="dimmed" size="sm">{rows.length} records</Text></div>{!r.noCreate&&<Button leftSection={<IconPlus size={16}/>} onClick={open}>New {r.label.replace(/s$/,'')}</Button>}</Group>
    {err&&<Alert color="red">{err}</Alert>}
    <Paper withBorder><Table.ScrollContainer minWidth={700}><Table striped highlightOnHover><Table.Thead><Table.Tr>{r.columns.map(c=><Table.Th key={c}>{c.replace(/([A-Z])/g,' $1')}</Table.Th>)}<Table.Th/></Table.Tr></Table.Thead>
      <Table.Tbody>{rows.length===0&&<Table.Tr><Table.Td colSpan={r.columns.length+1}><Text c="dimmed" ta="center" py="lg">No records yet</Text></Table.Td></Table.Tr>}
      {rows.map((row:any)=><Table.Tr key={row.id}>{r.columns.map(c=>{const v=row[c];return <Table.Td key={c}>{v===null||v===undefined?'—':typeof v==='object'?JSON.stringify(v).slice(0,70):String(v).slice(0,90)}</Table.Td>;})}
        <Table.Td><Group gap={4}>{r.actions?.map(a=><Button key={a.label} size="xs" variant="light" onClick={()=>run(a,row.id)}>{a.label}</Button>)}</Group></Table.Td></Table.Tr>)}</Table.Tbody></Table></Table.ScrollContainer></Paper>
    <Modal opened={opened} onClose={close} title={`New ${r.label.replace(/s$/,'')}`} size="lg"><Stack gap="sm">
      {r.fields.map(f=>f.type==='select'?<Select key={f.name} label={f.label} data={f.options??[]} value={values[f.name]??''} onChange={v=>setValues(s=>({...s,[f.name]:v}))}/>
        :f.type==='number'?<NumberInput key={f.name} label={f.label} value={values[f.name]??''} onChange={v=>setValues(s=>({...s,[f.name]:v}))}/>
        :f.type==='textarea'?<Textarea key={f.name} label={f.label} value={values[f.name]??''} onChange={e=>setValues(s=>({...s,[f.name]:e.currentTarget.value}))}/>
        :f.type==='boolean'?<Checkbox key={f.name} label={f.label} checked={!!values[f.name]} onChange={e=>setValues(s=>({...s,[f.name]:e.currentTarget.checked}))}/>
        :<TextInput key={f.name} label={f.label} value={values[f.name]??''} onChange={e=>setValues(s=>({...s,[f.name]:e.currentTarget.value}))}/>)}
      <Group justify="flex-end"><Button variant="subtle" onClick={close}>Cancel</Button><Button loading={busy} onClick={submit}>Create</Button></Group>
    </Stack></Modal>
  </Stack>;
}
