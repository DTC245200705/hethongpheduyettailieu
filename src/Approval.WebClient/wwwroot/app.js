const state = {
  currentUser: localStorage.getItem('docApproveUser') || 'vinh.duong',
  selectedDocId: null,
  documents: [],
  notifications: [],
  hubConnection: null,
  config: {
    notificationHubUrl: 'http://localhost:5002/hubs/notifications',
    documentApiDocsUrl: 'http://localhost:5001/swagger'
  }
};

const el = (id) => document.getElementById(id);

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function formatDate(iso) {
  if (!iso) return '';
  return new Date(iso).toLocaleString('vi-VN', {
    hour12: false,
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit'
  });
}

function showToast(message, type = 'info') {
  const container = el('toastContainer');
  const div = document.createElement('div');
  div.className = `toast ${type}`;
  div.textContent = message;
  container.appendChild(div);
  setTimeout(() => div.remove(), 5000);
}

async function apiFetch(url, options = {}) {
  const res = await fetch(url, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers || {})
    }
  });
  const text = await res.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = { raw: text }; }
  if (!res.ok) {
    throw new Error(data?.error || `HTTP ${res.status}`);
  }
  return data;
}

async function init() {
  try {
    state.config = await apiFetch('/api/client-config');
    el('swaggerLink').href = state.config.documentApiDocsUrl;
  } catch (err) {
    console.warn('Could not fetch client config:', err);
  }

  initUserSwitcher();
  bindEvents();
  await Promise.all([loadDocuments(), loadNotifications()]);
  await connectSignalR();
}

function initUserSwitcher() {
  const select = el('currentUserSelect');
  const customInput = el('customUserInput');
  const presetValues = ['vinh.duong', 'truong.khoa', 'giam.doc'];

  if (presetValues.includes(state.currentUser)) {
    select.value = state.currentUser;
  } else {
    select.value = 'custom';
    customInput.value = state.currentUser;
    customInput.classList.remove('hidden');
  }
  el('drawerUserLabel').textContent = state.currentUser;

  select.addEventListener('change', async () => {
    const oldUser = state.currentUser;
    if (select.value === 'custom') {
      customInput.classList.remove('hidden');
      customInput.focus();
      return;
    }
    customInput.classList.add('hidden');
    await switchUser(oldUser, select.value);
  });

  customInput.addEventListener('change', async () => {
    const val = customInput.value.trim();
    if (!val) return;
    await switchUser(state.currentUser, val);
  });
}

async function switchUser(oldUser, newUser) {
  state.currentUser = newUser;
  localStorage.setItem('docApproveUser', newUser);
  el('drawerUserLabel').textContent = newUser;

  if (state.hubConnection && state.hubConnection.state === signalR.HubConnectionState.Connected) {
    try {
      await state.hubConnection.invoke('LeaveUserGroup', oldUser);
      await state.hubConnection.invoke('JoinUserGroup', newUser);
    } catch (e) { console.warn(e); }
  }

  await Promise.all([loadDocuments(), loadNotifications()]);
  if (state.selectedDocId) {
    await loadDocumentDetail(state.selectedDocId);
  }
  showToast(`Đã chuyển tài khoản sang: ${newUser}`);
}

function bindEvents() {
  el('filterStatus').addEventListener('change', loadDocuments);
  el('filterScope').addEventListener('change', loadDocuments);
  el('filterSearch').addEventListener('input', debounce(loadDocuments, 300));
  el('btnRefresh').addEventListener('click', () => {
    loadDocuments();
    loadNotifications();
    if (state.selectedDocId) loadDocumentDetail(state.selectedDocId);
  });

  el('btnOpenCreate').addEventListener('click', () => {
    el('docCreatedBy').value = state.currentUser;
    el('createModal').classList.remove('hidden');
  });

  document.querySelectorAll('[data-close]').forEach(btn => {
    btn.addEventListener('click', () => el(btn.dataset.close).classList.add('hidden'));
  });

  el('createForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    try {
      const created = await apiFetch('/api/documents', {
        method: 'POST',
        body: JSON.stringify({
          title: el('docTitle').value.trim(),
          content: el('docContent').value.trim(),
          createdBy: el('docCreatedBy').value.trim(),
          assignedApprover: el('docApprover').value.trim()
        })
      });
      el('createForm').reset();
      el('createModal').classList.add('hidden');
      showToast('Đã tạo tài liệu nháp thành công!', 'success');
      await loadDocuments();
      await loadDocumentDetail(created.id);
    } catch (err) {
      showToast(err.message, 'error');
    }
  });

  el('btnNotifications').addEventListener('click', () => {
    el('notificationDrawer').classList.toggle('hidden');
    loadNotifications();
  });
  el('btnCloseDrawer').addEventListener('click', () => el('notificationDrawer').classList.add('hidden'));
  el('btnMarkAllRead').addEventListener('click', async () => {
    try {
      await apiFetch(`/api/notifications/read-all?recipient=${encodeURIComponent(state.currentUser)}`, { method: 'POST' });
      await loadNotifications();
    } catch (err) { showToast(err.message, 'error'); }
  });

  el('btnSystemStatus').addEventListener('click', async () => {
    el('healthModal').classList.remove('hidden');
    await loadSystemHealth();
  });
  el('btnRecheckHealth').addEventListener('click', loadSystemHealth);
}

async function loadDocuments() {
  const params = new URLSearchParams();
  const status = el('filterStatus').value;
  const scope = el('filterScope').value;
  const search = el('filterSearch').value.trim();

  if (status) params.set('status', status);
  if (scope === 'createdBy') params.set('createdBy', state.currentUser);
  if (scope === 'approver') params.set('approver', state.currentUser);
  if (search) params.set('search', search);

  try {
    const docs = await apiFetch(`/api/documents?${params.toString()}`);
    state.documents = docs;
    renderDocumentsTable(docs);
    updateStats(docs);
  } catch (err) {
    el('documentsBody').innerHTML = `<tr><td colspan="5" class="empty-cell">Không thể tải dữ liệu: ${escapeHtml(err.message)}</td></tr>`;
  }
}

function updateStats(docs) {
  el('statTotal').textContent = docs.length;
  el('statDraft').textContent = docs.filter(d => d.status === 'Draft').length;
  el('statSubmitted').textContent = docs.filter(d => d.status === 'Submitted').length;
  el('statApproved').textContent = docs.filter(d => d.status === 'Approved').length;
  el('statRejected').textContent = docs.filter(d => d.status === 'Rejected').length;
}

function renderDocumentsTable(docs) {
  const tbody = el('documentsBody');
  if (!docs.length) {
    tbody.innerHTML = '<tr><td colspan="5" class="empty-cell">Chưa có tài liệu nào khớp bộ lọc.</td></tr>';
    return;
  }

  tbody.innerHTML = docs.map(doc => `
    <tr class="${doc.id === state.selectedDocId ? 'selected' : ''}" data-id="${doc.id}">
      <td><strong>${escapeHtml(doc.title)}</strong></td>
      <td>${escapeHtml(doc.createdBy)}</td>
      <td>${escapeHtml(doc.assignedApprover)}</td>
      <td><span class="status-tag status-${doc.status}">${doc.status}</span></td>
      <td>${formatDate(doc.updatedAtUtc)}</td>
    </tr>
  `).join('');

  tbody.querySelectorAll('tr[data-id]').forEach(row => {
    row.addEventListener('click', () => loadDocumentDetail(row.dataset.id));
  });
}

async function loadDocumentDetail(id) {
  state.selectedDocId = id;
  document.querySelectorAll('#documentsBody tr').forEach(r => r.classList.toggle('selected', r.dataset.id === id));

  try {
    const doc = await apiFetch(`/api/documents/${id}`);
    renderDetailPanel(doc);
  } catch (err) {
    el('detailPanel').innerHTML = `<div class="empty-detail">Lỗi tải chi tiết: ${escapeHtml(err.message)}</div>`;
  }
}

function renderDetailPanel(doc) {
  const isCreator = doc.createdBy.toLowerCase() === state.currentUser.toLowerCase();
  const isApprover = doc.assignedApprover.toLowerCase() === state.currentUser.toLowerCase();

  let actionsHtml = '';
  if ((doc.status === 'Draft' || doc.status === 'Rejected') && isCreator) {
    actionsHtml = `
      <div class="action-box">
        <strong>Thao tác của người tạo (${escapeHtml(state.currentUser)})</strong>
        <textarea id="actionComment" rows="2" placeholder="Ghi chú gửi duyệt (tùy chọn)..."></textarea>
        <div class="action-buttons">
          <button class="btn btn-warning" onclick="performAction('${doc.id}', 'submit')">Gửi phê duyệt (Submit)</button>
        </div>
      </div>`;
  } else if (doc.status === 'Submitted' && isApprover) {
    actionsHtml = `
      <div class="action-box">
        <strong>Thao tác của người phê duyệt (${escapeHtml(state.currentUser)})</strong>
        <textarea id="actionComment" rows="2" placeholder="Nhập nhận xét phê duyệt hoặc lý do từ chối (bắt buộc khi từ chối)..."></textarea>
        <div class="action-buttons">
          <button class="btn btn-success" onclick="performAction('${doc.id}', 'approve')">✓ Phê duyệt (Approve)</button>
          <button class="btn btn-danger" onclick="performAction('${doc.id}', 'reject')">✕ Từ chối (Reject)</button>
        </div>
      </div>`;
  } else if (doc.status !== 'Approved') {
    actionsHtml = `
      <div class="action-box">
        <span style="font-size:0.84rem;color:var(--muted)">
          Chuyển sang tài khoản <strong>${doc.status === 'Submitted' ? escapeHtml(doc.assignedApprover) : escapeHtml(doc.createdBy)}</strong> ở góc trên để thực hiện bước tiếp theo.
        </span>
      </div>`;
  }

  const histories = (doc.histories || []).map(h => `
    <li>
      <div><strong>${escapeHtml(h.action)}</strong> (${escapeHtml(h.fromStatus)} → ${escapeHtml(h.toStatus)}) bởi <strong>${escapeHtml(h.actor)}</strong></div>
      <div class="timeline-time">${formatDate(h.createdAtUtc)}</div>
      ${h.comment ? `<div class="timeline-comment">"${escapeHtml(h.comment)}"</div>` : ''}
    </li>
  `).join('');

  el('detailPanel').innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:start;gap:0.75rem;">
      <h3>${escapeHtml(doc.title)}</h3>
      <span class="status-tag status-${doc.status}">${doc.status}</span>
    </div>
    <div class="detail-meta">
      <div><span style="color:var(--muted)">Mã tài liệu:</span><br/><code>${doc.id.slice(0, 8)}...</code></div>
      <div><span style="color:var(--muted)">Người tạo:</span><br/><strong>${escapeHtml(doc.createdBy)}</strong></div>
      <div><span style="color:var(--muted)">Người phê duyệt:</span><br/><strong>${escapeHtml(doc.assignedApprover)}</strong></div>
      <div><span style="color:var(--muted)">Cập nhật lúc:</span><br/>${formatDate(doc.updatedAtUtc)}</div>
    </div>
    <div class="detail-content">${escapeHtml(doc.content)}</div>
    ${actionsHtml}
    <h4 style="font-size:0.9rem;">Lịch sử xử lý (${(doc.histories || []).length} bước)</h4>
    <ul class="timeline">${histories}</ul>
  `;
}

window.performAction = async function(id, action) {
  const comment = el('actionComment')?.value?.trim() || '';
  if (action === 'reject' && !comment) {
    showToast('Vui lòng nhập lý do từ chối trước khi bấm Reject.', 'error');
    return;
  }
  try {
    await apiFetch(`/api/documents/${id}/${action}`, {
      method: 'POST',
      body: JSON.stringify({ actor: state.currentUser, comment })
    });
    showToast(`Thao tác ${action.toUpperCase()} thành công! Sự kiện đang được phát qua RabbitMQ.`, 'success');
    await loadDocuments();
    await loadDocumentDetail(id);
  } catch (err) {
    showToast(err.message, 'error');
  }
};

async function loadNotifications() {
  try {
    const items = await apiFetch(`/api/notifications?recipient=${encodeURIComponent(state.currentUser)}`);
    state.notifications = items;
    renderNotifications(items);
  } catch (err) {
    console.warn('Notification service unavailable:', err.message);
  }
}

function renderNotifications(items) {
  const unreadCount = items.filter(n => !n.isRead).length;
  const badge = el('unreadBadge');
  badge.textContent = unreadCount;
  badge.classList.toggle('hidden', unreadCount === 0);

  const list = el('notificationList');
  if (!items.length) {
    list.innerHTML = '<div class="empty-detail">Chưa có thông báo nào cho tài khoản này.</div>';
    return;
  }

  list.innerHTML = items.map(n => `
    <div class="noti-item ${n.isRead ? '' : 'unread'}" data-nid="${n.id}" data-docid="${n.documentId}">
      <div>${escapeHtml(n.message)}</div>
      <div class="noti-meta">
        <span>${escapeHtml(n.eventType)}</span>
        <span>${formatDate(n.createdAtUtc)}</span>
      </div>
    </div>
  `).join('');

  list.querySelectorAll('.noti-item').forEach(item => {
    item.addEventListener('click', async () => {
      try { await apiFetch(`/api/notifications/${item.dataset.nid}/read`, { method: 'POST' }); } catch {}
      await loadNotifications();
      await loadDocumentDetail(item.dataset.docid);
      el('notificationDrawer').classList.add('hidden');
    });
  });
}

async function connectSignalR() {
  const statusEl = el('signalrStatus');
  if (typeof signalR === 'undefined') {
    statusEl.textContent = '● SignalR: Chế độ Polling';
    statusEl.className = 'status-pill reconnecting';
    setInterval(loadNotifications, 5000);
    return;
  }

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(state.config.notificationHubUrl)
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .build();

  state.hubConnection = connection;

  connection.on('ReceiveNotification', (dto) => {
    showToast(`🔔 ${dto.message}`, 'info');
    loadNotifications();
  });

  connection.on('DocumentUpdated', (docId) => {
    loadDocuments();
    if (state.selectedDocId === docId) {
      loadDocumentDetail(docId);
    }
  });

  connection.onreconnecting(() => {
    statusEl.textContent = '● SignalR: Đang kết nối lại...';
    statusEl.className = 'status-pill reconnecting';
  });

  connection.onreconnected(async () => {
    statusEl.textContent = '● SignalR: Trực tuyến';
    statusEl.className = 'status-pill online';
    await connection.invoke('JoinUserGroup', state.currentUser);
    await loadNotifications();
  });

  connection.onclose(() => {
    statusEl.textContent = '● SignalR: Ngoại tuyến';
    statusEl.className = 'status-pill offline';
  });

  const startLoop = async () => {
    try {
      await connection.start();
      statusEl.textContent = '● SignalR: Trực tuyến';
      statusEl.className = 'status-pill online';
      await connection.invoke('JoinUserGroup', state.currentUser);
    } catch {
      statusEl.textContent = '● SignalR: Đang thử kết nối...';
      statusEl.className = 'status-pill reconnecting';
      setTimeout(startLoop, 4000);
    }
  };

  await startLoop();
}

async function loadSystemHealth() {
  const box = el('healthContent');
  box.innerHTML = 'Đang truy vấn các service...';
  try {
    const res = await apiFetch('/api/system/status');
    box.innerHTML = `
      <div class="health-card">
        <strong>Document.Api (/health)</strong>
        <pre>${escapeHtml(JSON.stringify(res.documentApi, null, 2))}</pre>
      </div>
      <div class="health-card">
        <strong>Transactional Outbox (/outbox/stats)</strong>
        <pre>${escapeHtml(JSON.stringify(res.outbox, null, 2))}</pre>
      </div>
      <div class="health-card">
        <strong>Notification.Service (/health)</strong>
        <pre>${escapeHtml(JSON.stringify(res.notificationService, null, 2))}</pre>
      </div>
    `;
  } catch (err) {
    box.innerHTML = `<div class="health-card">Lỗi kiểm tra trạng thái: ${escapeHtml(err.message)}</div>`;
  }
}

function debounce(fn, ms) {
  let timer;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), ms);
  };
}

init();
