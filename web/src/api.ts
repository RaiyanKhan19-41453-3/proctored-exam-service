export const TOKEN_KEY = 'exam-token';
export const getToken = () => localStorage.getItem(TOKEN_KEY) ?? '';
export const setToken = (t:string) => localStorage.setItem(TOKEN_KEY,t);
export const clearToken = () => localStorage.removeItem(TOKEN_KEY);
export async function api<T=any>(path:string, o:{method?:string;body?:unknown}={}):Promise<T>{
  const r=await fetch(path,{method:o.method??(o.body!==undefined?'POST':'GET'),headers:{'Content-Type':'application/json',Authorization:`Bearer ${getToken()}`},body:o.body!==undefined?JSON.stringify(o.body):undefined});
  const text=await r.text();
  if(!r.ok){ let m=`${r.status} ${r.statusText}`; try{const j=JSON.parse(text);m=j.detail?.error??j.detail??j.message??j.title??m;}catch{if(text)m=text.slice(0,200);} throw new Error(m); }
  return text?JSON.parse(text):null as T;
}
export const asArray = (v:unknown):any[] => Array.isArray(v)?v:(v&&typeof v==='object'&&'items'in v)?(v as any).items:[];
