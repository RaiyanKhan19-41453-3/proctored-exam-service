import { useState } from 'react';
import { MantineProvider, createTheme } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { getToken } from './api';
import { Layout } from './components/Layout';
import { Login } from './components/Login';
import { Dashboard } from './components/Dashboard';
import { ExamsPage } from './components/ExamsPage';
import { AssignmentsPage } from './components/AssignmentsPage';
import { CandidatePage } from './components/CandidatePage';
import { ReportsPage } from './components/ReportsPage';
import { ProctorPage } from './components/ProctorPage';
import { ResourcePage } from './components/ResourcePage';

const theme=createTheme({primaryColor:'teal',fontFamily:'Inter, system-ui, sans-serif',defaultRadius:'md'});
const flags={key:'flags',label:'Anomaly flags',path:'/api/proctor/exams/00000000-0000-0000-0000-000000000000/flags',fields:[],columns:['kind','score','reviewed'],noCreate:true};
export default function App(){
  const [token,setToken]=useState(getToken());
  if(!token)return <MantineProvider theme={theme}><Notifications/><Login onSuccess={()=>setToken(getToken())}/></MantineProvider>;
  return <MantineProvider theme={theme}><Notifications/><BrowserRouter><Layout><Routes>
    <Route path="/" element={<Dashboard/>}/>
    <Route path="/exams" element={<ExamsPage/>}/>
    <Route path="/assignments" element={<AssignmentsPage/>}/>
    <Route path="/take" element={<CandidatePage/>}/>
    <Route path="/reports" element={<ReportsPage/>}/>
    <Route path="/proctor" element={<ProctorPage/>}/>
    <Route path="/flags" element={<ResourcePage r={flags}/>}/>
    <Route path="*" element={<Navigate to="/"/>}/>
  </Routes></Layout></BrowserRouter></MantineProvider>;
}
