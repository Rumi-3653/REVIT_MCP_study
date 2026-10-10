// 回歸測試（不需要 Revit，用本機假伺服器）：
// Revit 端切換 AI 客戶端／停止服務時會送 close frame（reason=switched / service-stopped），
// node 端必須 (1) 立刻讓等待中的命令失敗，而不是等 30 秒逾時 (2) 不自動重連；
// 握手被 409 拒絕時也不得進入每 5 秒重連的迴圈。
// 先 `npm run build`，再 `npm run test:socket-switch`。
import http from 'node:http';
import { WebSocketServer } from 'ws';
import { RevitSocketClient } from '../build/socket.js';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function assert(cond, msg) {
  if (!cond) { console.error('FAIL -', msg); process.exit(1); }
  console.log('ok   -', msg);
}

// --- Case 1：連上後被 Revit 以 reason=switched 踢掉 ---
{
  let connections = 0;
  const wss = new WebSocketServer({ port: 0, host: '127.0.0.1' });
  await new Promise((r) => wss.once('listening', r));
  wss.on('connection', () => { connections++; });

  const client = new RevitSocketClient('127.0.0.1', wss.address().port);
  await client.connect();
  const t0 = Date.now();
  const pending = client.sendCommand('ping', {}, 30000).catch((e) => e);
  await sleep(100);
  for (const ws of wss.clients) ws.close(1000, 'switched');
  const err = await pending;
  const elapsed = Date.now() - t0;
  assert(err instanceof Error && /切換/.test(err.message), `pending command fails with the switch message (${elapsed} ms)`);
  assert(elapsed < 5000, 'failed well under the 30 s command timeout');
  await sleep(6500); // 超過 reconnectInterval(5 s)
  assert(connections === 1, `no auto-reconnect after 'switched' (connections=${connections})`);
  assert(!client.isConnected(), 'client reports disconnected');
  wss.close();
}

// --- Case 2：握手被 409 拒絕 → 不得每 5 秒重連 ---
{
  let attempts = 0;
  const server = http.createServer();
  server.on('upgrade', (_req, socket) => {
    attempts++;
    socket.end('HTTP/1.1 409 Conflict\r\nConnection: close\r\nContent-Length: 0\r\n\r\n');
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));

  const client = new RevitSocketClient('127.0.0.1', server.address().port);
  const err = await client.connect().catch((e) => e);
  assert(err instanceof Error && /409/.test(err.message), '409 surfaces as the actionable 409 message');
  await sleep(6500);
  assert(attempts === 1, `no reconnect loop after 409 (attempts=${attempts})`);
  server.close();
}

console.log('PASS test-socket-switch');
process.exit(0);
