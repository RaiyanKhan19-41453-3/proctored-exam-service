import { useEffect, useState } from 'react';
import { Stack, Title, SimpleGrid, Card, Text, Group, Badge, Button } from '@mantine/core';
import { Link } from 'react-router-dom';
import { api, asArray } from '../api';
export function Dashboard(){
  const [exams,setExams]=useState<any[]>([]),[reports,setReports]=useState<any[]>([]);
  useEffect(()=>{api('/api/exams').then(r=>setExams(asArray(r))).catch(()=>{});api('/api/reports').then(r=>setReports(asArray(r))).catch(()=>{});},[]);
  const cards=[{label:'Exams',value:exams.length,to:'/exams'},{label:'Published',value:exams.filter(e=>e.status===1).length,to:'/exams'},{label:'Candidate reports',value:reports.length,to:'/reports'},{label:'Flagged',value:reports.filter(r=>r.decision===2).length,to:'/reports'}];
  return <Stack gap="md"><Group justify="space-between"><div><Title order={2}>Overview</Title><Text c="dimmed">Exam delivery and review queue</Text></div><Badge color="teal" variant="light">Standalone service</Badge></Group>
    <SimpleGrid cols={{base:1,sm:2,lg:4}}>{cards.map(c=><Card key={c.label} withBorder component={Link} to={c.to} style={{textDecoration:'none',color:'inherit'}}><Text size="sm" c="dimmed">{c.label}</Text><Title order={2} mt="xs">{c.value}</Title></Card>)}</SimpleGrid>
    <Card withBorder><Title order={4} mb="sm">Quick actions</Title><Group><Button component={Link} to="/exams" variant="light">Create an exam</Button><Button component={Link} to="/assignments" variant="light">Assign candidates</Button><Button component={Link} to="/proctor" variant="light">Open live proctor</Button><Button component={Link} to="/take" variant="light">Take an exam (candidate)</Button></Group></Card>
  </Stack>;
}
