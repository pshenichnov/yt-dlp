const http = require('node:http'); const fs = require('node:fs'); const path = require('node:path');
const root = path.resolve(__dirname, '../../src/Api/YouTubeProcessing.Api/wwwroot');
const services = Object.fromEntries(['api','postgres','rabbitMq','blobStore','orchestrator','extractor','analyzer'].map((key) => [key,{ status:key === 'analyzer' ? 'disabled':'healthy', durationMilliseconds:4, description:`${key} fixture` }]));
let mode = 'healthy';
http.createServer((request,response) => {
  const url = new URL(request.url,'http://localhost');
  if (url.pathname === '/__mode') { mode=url.searchParams.get('value') || 'healthy'; response.end('ok'); return; }
  if (url.pathname === '/health') {
    if (mode === 'unavailable') { response.writeHead(500); response.end('failed'); return; }
    const payload = { status:mode === 'unhealthy' ? 'unhealthy':'healthy', checkedAt:'2026-11-24T12:00:00Z', services:JSON.parse(JSON.stringify(services)) };
    if (mode === 'unhealthy') payload.services.rabbitMq={ status:'unhealthy', durationMilliseconds:12, description:'No consumers', queues:{extract:{status:'unhealthy',consumers:0}} };
    if (mode === 'malformed') { payload.services.postgres=null; payload.services.extractor={status:'surprise',queues:{broken:null,events:{consumers:'many'}}}; }
    response.writeHead(mode === 'unhealthy' ? 503:200, {'Content-Type':'application/json'}); response.end(JSON.stringify(payload)); return;
  }
  const requested = url.pathname === '/' ? 'index.html' : url.pathname === '/status' || url.pathname === '/health.html' ? 'health.html' : url.pathname.slice(1);
  const file = path.join(root,requested); const types={'.html':'text/html','.js':'text/javascript','.css':'text/css'};
  fs.readFile(file,(error,data)=>{ if(error){response.writeHead(404); response.end('not found');return;} response.writeHead(200,{'Content-Type':types[path.extname(file)]||'application/octet-stream'});response.end(data); });
}).listen(4174,'127.0.0.1');
