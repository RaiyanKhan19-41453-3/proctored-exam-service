import { useState } from 'react';
import { Center, Paper, Title, TextInput, Button, Stack, Text, PasswordInput, Alert } from '@mantine/core';
import { setToken } from '../api';
export function Login({onSuccess}:{onSuccess:()=>void}){
  const [secret,setSecret]=useState(''),[method,setMethod]=useState('token');
  const [email,setEmail]=useState('admin@star.local'),[password,setPassword]=useState('Password123!'),[slug,setSlug]=useState('star-coaching'),[err,setErr]=useState('');
  const login=async()=>{setErr('');try{
    if(method==='token'){if(!secret.trim())throw new Error('Paste a token');setToken(secret.trim());}
    else{const r=await fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email,password,tenantSlug:slug})});const j=await r.json();if(!r.ok)throw new Error(j.detail??j.title??'Login failed');setToken(j.accessToken);}
    onSuccess();
  }catch(e:any){setErr(e.message);}};
  return <Center mih="80vh"><Paper withBorder shadow="md" p="xl" w={440}><Stack gap="md">
    <div><Title order={2}>Proctored Exams</Title><Text c="dimmed" size="sm">Examiner or candidate access</Text></div>{err&&<Alert color="red">{err}</Alert>}
    <Button variant={method==='token'?'filled':'light'} onClick={()=>setMethod('token')}>Use access token</Button>
    <Button variant={method==='login'?'filled':'light'} onClick={()=>setMethod('login')}>Login via main platform</Button>
    {method==='token'?<TextInput label="Access token" value={secret} onChange={e=>setSecret(e.currentTarget.value)} placeholder="eyJhbGci…"/>:<Stack gap="sm"><TextInput label="Email" value={email} onChange={e=>setEmail(e.currentTarget.value)}/><PasswordInput label="Password" value={password} onChange={e=>setPassword(e.currentTarget.value)}/><TextInput label="Tenant slug" value={slug} onChange={e=>setSlug(e.currentTarget.value)}/></Stack>}
    <Button onClick={login} fullWidth>Continue</Button>
  </Stack></Paper></Center>;
}
