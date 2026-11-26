import { AppShell, NavLink, Group, Title, Button, Badge } from '@mantine/core';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { IconGauge, IconClipboardList, IconUserPlus, IconChartBar, IconShield, IconLogout } from '@tabler/icons-react';
import { clearToken } from '../api';
const nav=[
  {to:'/',label:'Overview',icon:IconGauge},
  {to:'/exams',label:'Exams',icon:IconClipboardList},
  {to:'/assignments',label:'Assignments',icon:IconUserPlus},
  {to:'/reports',label:'Reports',icon:IconChartBar},
  {to:'/proctor',label:'Live proctor',icon:IconShield},
];
export function Layout({children}:{children:React.ReactNode}){
  const loc=useLocation(), navg=useNavigate();
  return <AppShell header={{height:60}} navbar={{width:230,breakpoint:'sm'}} padding="md">
    <AppShell.Header><Group h="100%" px="md" justify="space-between">
      <Group><Title order={4}>Proctored Exams</Title><Badge color="teal" variant="light">Microservice</Badge></Group>
      <Button variant="subtle" color="red" leftSection={<IconLogout size={16}/>} onClick={()=>{clearToken();navg('/login');}}>Sign out</Button>
    </Group></AppShell.Header>
    <AppShell.Navbar p="md">{nav.map(n=><NavLink key={n.to} component={Link} to={n.to} label={n.label} leftSection={<n.icon size={18}/>} active={loc.pathname===n.to||(n.to!=='/'&&loc.pathname.startsWith(n.to))} mb={2}/>)}</AppShell.Navbar>
    <AppShell.Main>{children}</AppShell.Main>
  </AppShell>;
}
