import { useState, useEffect, useRef, useCallback, startTransition } from 'react';

// Proxy destekli fetch yardımcısı
// Tarayıcı ortamında gerçek proxy tüneli kurulamaz; proxy bilgisi
// ileride bir Electron/Node katmanına iletilebilir. Şimdilik doğrudan fetch.
async function proxyFetch(url, options = {}) {
  return fetch(url, options);
}

// Electron IPC — tarayıcıda çalışırken güvenli şekilde devre dışı kalır
const ipcRenderer = window?.require?.('electron')?.ipcRenderer ?? null;

const EMPTY_SERVER = { id: null, name: '', url: '', proxy: { host: '', port: '', user: '', pass: '' } };

function App() {
  const [activeTab, setActiveTab] = useState('banned');
  const [stats, setStats] = useState({ totalBans: 0, activeBans: 0, reportedBans: 0, todayBans: 0 });
  const [bans, setBans] = useState([]);
  const [whitelist, setWhitelist] = useState([]);
  const [logs, setLogs] = useState([]);
  const logsEndRef = useRef(null);
  const [autoScroll, setAutoScroll] = useState(true);
  const [logSearch, setLogSearch] = useState('');

  // Auto-updater state
  const [updateStatus, setUpdateStatus] = useState(null); // null | 'checking' | 'available' | 'latest' | 'downloaded' | 'error'
  const [updateVersion, setUpdateVersion] = useState('');
  const [updateProgress, setUpdateProgress] = useState(null); // { percent, bytesPerSecond }
  const [showUpdateBar, setShowUpdateBar] = useState(false);

  // Modal States
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [modalIp, setModalIp] = useState('');
  const [modalReason, setModalReason] = useState('');

  // Search State
  const [searchTerm, setSearchTerm] = useState('');
  const [filterReason, setFilterReason] = useState('');

  // Pagination State
  const [currentPage, setCurrentPage] = useState(1);

  // Server Modal State — hem ekle hem düzenle için tek modal
  const [isServerModalOpen, setIsServerModalOpen] = useState(false);
  const [serverForm, setServerForm] = useState(EMPTY_SERVER);
  const isEditMode = serverForm.id !== null;

  // API Status State
  const [isApiConnected, setIsApiConnected] = useState(false);

  // Server Management
  const [servers, setServers] = useState(() => {
    const saved = localStorage.getItem('fail2ban_servers');
    if (saved) {
      const parsed = JSON.parse(saved);
      // Eski kayıtlarda proxy alanı yoksa ekle
      return parsed.map(s => ({ proxy: { host: '', port: '', user: '', pass: '' }, ...s }));
    }
    const defaultUrl = import.meta.env.VITE_API_URL || 'http://localhost:5009/api';
    return [{ id: 1, name: 'Ana Sunucu', url: defaultUrl, proxy: { host: '', port: '', user: '', pass: '' } }];
  });
  const [activeServerId, setActiveServerId] = useState(servers[0]?.id || 1);
  const activeServer = servers.find(s => s.id === activeServerId) || servers[0];
  const API_BASE_URL = activeServer?.url || 'http://localhost:5009/api';

  useEffect(() => {
    localStorage.setItem('fail2ban_servers', JSON.stringify(servers));
  }, [servers]);

  // Auto-updater IPC listeners
  useEffect(() => {
    if (!ipcRenderer) return;

    const onStatus = (_, data) => {
      setUpdateStatus(data.status);
      if (data.version) setUpdateVersion(data.version);
      if (data.status === 'available' || data.status === 'downloaded') setShowUpdateBar(true);
      if (data.status === 'error') setShowUpdateBar(true);
    };

    const onProgress = (_, data) => {
      setUpdateProgress(data);
      setShowUpdateBar(true);
    };

    ipcRenderer.on('updater:status', onStatus);
    ipcRenderer.on('updater:progress', onProgress);

    return () => {
      ipcRenderer.removeListener('updater:status', onStatus);
      ipcRenderer.removeListener('updater:progress', onProgress);
    };
  }, []);

  const fetchStats = useCallback(async () => {
    try {
      const res = await proxyFetch(`${API_BASE_URL}/stats`);
      const data = await res.json();
      setStats(data);
      setIsApiConnected(true);
    } catch {
      setIsApiConnected(false);
    }
  }, [API_BASE_URL]);

  const fetchBans = useCallback(async () => {
    try {
      const res = await proxyFetch(`${API_BASE_URL}/bans`);
      const data = await res.json();
      setBans(data);
    } catch { /* bağlantı hatası sessizce geçilir */ }
  }, [API_BASE_URL]);

  const fetchWhitelist = useCallback(async () => {
    try {
      const res = await proxyFetch(`${API_BASE_URL}/whitelist`);
      const data = await res.json();
      setWhitelist(data);
    } catch { /* bağlantı hatası sessizce geçilir */ }
  }, [API_BASE_URL]);

  const fetchLogs = useCallback(async () => {
    try {
      const res = await proxyFetch(`${API_BASE_URL}/logs`);
      const data = await res.json();
      setLogs(data);
    } catch { /* bağlantı hatası sessizce geçilir */ }
  }, [API_BASE_URL]);

  useEffect(() => {
    startTransition(() => {
      fetchStats();
      if (activeTab === 'banned') fetchBans();
      if (activeTab === 'whitelist') fetchWhitelist();
      if (activeTab === 'logs') fetchLogs();
    });

    const interval = setInterval(() => {
      fetchStats();
      if (activeTab === 'banned') fetchBans();
      if (activeTab === 'whitelist') fetchWhitelist();
      if (activeTab === 'logs') fetchLogs();
    }, 3000);

    return () => clearInterval(interval);
  }, [activeTab, activeServerId, API_BASE_URL, fetchStats, fetchBans, fetchWhitelist, fetchLogs]);

  // Sunucu değişince ekranı temizle
  const prevServerIdRef = useRef(activeServerId);
  useEffect(() => {
    if (prevServerIdRef.current !== activeServerId) {
      prevServerIdRef.current = activeServerId;
      startTransition(() => {
        setBans([]);
        setWhitelist([]);
        setLogs([]);
        setStats({ totalBans: 0, activeBans: 0, reportedBans: 0, todayBans: 0 });
      });
    }
  }, [activeServerId]);

  useEffect(() => {
    if (autoScroll && activeTab === 'logs' && logsEndRef.current) {
      logsEndRef.current.scrollIntoView({ behavior: 'auto' });
    }
  }, [logs, activeTab, autoScroll]);

  const handeUnban = async (ip) => {
    try {
      await proxyFetch(`${API_BASE_URL}/bans/${ip}`, { method: 'DELETE' });
      fetchBans();
      fetchStats();
    } catch { /* sessiz */ }
  };

  const handleRemoveWhitelist = async (ip) => {
    try {
      await proxyFetch(`${API_BASE_URL}/whitelist/${ip}`, { method: 'DELETE' });
      fetchWhitelist();
    } catch { /* sessiz */ }
  };

  const handleAddToWhitelist = async (ip) => {
    try {
      await proxyFetch(`${API_BASE_URL}/whitelist?ip=${ip}&desc=Manuel_Eklendi`, { method: 'POST' });
      if (activeTab === 'banned') fetchBans();
    } catch { /* sessiz */ }
  };

  const handleSearchChange = (e) => { setSearchTerm(e.target.value); setCurrentPage(1); };
  const handleFilterChange = (e) => { setFilterReason(e.target.value); setCurrentPage(1); };

  // Sunucu modal helpers
  const openAddServer = () => { setServerForm(EMPTY_SERVER); setIsServerModalOpen(true); };
  const openEditServer = () => {
    const s = activeServer;
    setServerForm({ id: s.id, name: s.name, url: s.url, proxy: { ...{ host: '', port: '', user: '', pass: '' }, ...s.proxy } });
    setIsServerModalOpen(true);
  };
  const closeServerModal = () => { setIsServerModalOpen(false); setServerForm(EMPTY_SERVER); };

  const handleSaveServer = () => {
    if (!serverForm.name || !serverForm.url) return;
    if (isEditMode) {
      setServers(prev => prev.map(s => s.id === serverForm.id ? { ...serverForm } : s));
    } else {
      const newServer = { ...serverForm, id: Date.now() };
      setServers(prev => [...prev, newServer]);
      setActiveServerId(newServer.id);
    }
    closeServerModal();
  };

  const handleDeleteServer = () => {
    if (!confirm('Bu sunucuyu silmek istiyor musunuz?')) return;
    const newServers = servers.filter(s => s.id !== activeServerId);
    setServers(newServers);
    setActiveServerId(newServers[0].id);
    closeServerModal();
  };

  const handleManualAdd = async () => {
    if (!modalIp) return;
    try {
      if (activeTab === 'whitelist') {
        await proxyFetch(`${API_BASE_URL}/whitelist?ip=${modalIp}&desc=${modalReason || 'Manuel Beyaz Liste'}`, { method: 'POST' });
        fetchWhitelist();
      } else {
        await proxyFetch(`${API_BASE_URL}/bans?ip=${modalIp}&reason=${modalReason || 'Manuel Engelleme'}&duration=1440`, { method: 'POST' });
        fetchBans();
        fetchStats();
      }
      setIsModalOpen(false);
      setModalIp('');
      setModalReason('');
    } catch (err) { console.error('Ekleme Hatası', err); }
  };

  const uniqueReasons = [...new Set(bans.map(b => b.reason))];
  const sortedBans = [...bans].sort((a, b) => new Date(b.bannedAt) - new Date(a.bannedAt));
  const filteredBans = sortedBans
    .filter(b => b.ipAddress.includes(searchTerm) || b.reason.toLowerCase().includes(searchTerm.toLowerCase()))
    .filter(b => filterReason === '' || b.reason === filterReason);

  const POSTS_PER_PAGE = 20;
  const totalPages = Math.ceil(filteredBans.length / POSTS_PER_PAGE);
  const displayedBans = filteredBans.slice((currentPage - 1) * POSTS_PER_PAGE, currentPage * POSTS_PER_PAGE);

  const filteredLogs = logSearch
    ? logs.filter(l => l.toLowerCase().includes(logSearch.toLowerCase()))
    : logs;

  const inputCls = "w-full bg-slate-900 border border-slate-700 rounded-md px-3 py-2 text-white text-sm focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 placeholder-slate-600";
  const labelCls = "block text-xs font-medium text-slate-400 mb-1";

  return (
    <div className="flex h-screen bg-slate-900 text-slate-100 font-sans">
      {/* Sidebar */}
      <aside className="w-64 bg-slate-800 border-r border-slate-700 flex flex-col">
        <div className="p-6 border-b border-slate-700 flex items-center space-x-3">
          <div className="w-8 h-8 rounded-lg bg-blue-600 flex items-center justify-center font-bold text-white shadow-lg shadow-blue-500/30">F</div>
          <span className="text-xl font-bold bg-clip-text text-transparent bg-linear-to-r from-blue-400 to-cyan-300">Fail2Ban</span>
        </div>

        <nav className="flex-1 p-4 space-y-2">
          {[
            { key: 'banned', label: "Banlı IP'ler", activeColor: 'bg-blue-600/10 text-blue-400', icon: 'M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z' },
            { key: 'whitelist', label: 'Beyaz Liste', activeColor: 'bg-green-600/10 text-green-400', icon: 'M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z' },
            { key: 'logs', label: 'Canlı Loglar', activeColor: 'bg-amber-600/10 text-amber-400', icon: 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z' },
          ].map(tab => (
            <button key={tab.key} onClick={() => setActiveTab(tab.key)}
              className={`w-full flex items-center space-x-3 px-4 py-3 rounded-lg transition-all ${activeTab === tab.key ? tab.activeColor + ' font-medium' : 'hover:bg-slate-700/50 text-slate-400 hover:text-slate-200'}`}>
              <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d={tab.icon} /></svg>
              <span>{tab.label}</span>
            </button>
          ))}
        </nav>

        <div className="p-4 border-t border-slate-700 space-y-3">
          <label className="text-xs text-slate-400 font-medium tracking-wider">AKTİF SUNUCU (API)</label>
          <select value={activeServerId}
            onChange={(e) => e.target.value === 'add_new' ? openAddServer() : setActiveServerId(Number(e.target.value))}
            className="w-full bg-slate-900 border border-slate-700 rounded-md p-2 text-sm text-slate-200 focus:ring-1 focus:ring-blue-500 focus:border-blue-500">
            {servers.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
            <option value="add_new" className="font-bold text-blue-400">+ Yeni Sunucu Bağla...</option>
          </select>

          <div className="flex space-x-2">
            <button onClick={openEditServer}
              className="flex-1 text-xs py-1.5 px-2 rounded bg-slate-700 hover:bg-slate-600 text-slate-300 transition-colors">
              ✏️ Düzenle
            </button>
            {servers.length > 1 && (
              <button onClick={() => { if (confirm('Bu sunucuyu silmek istiyor musunuz?')) { const ns = servers.filter(s => s.id !== activeServerId); setServers(ns); setActiveServerId(ns[0].id); } }}
                className="flex-1 text-xs py-1.5 px-2 rounded bg-rose-900/40 border border-rose-800 hover:bg-rose-800/60 text-rose-300 transition-colors">
                🗑 Sil
              </button>
            )}
          </div>

          <div className={`p-3 rounded border flex items-center space-x-3 transition-colors ${isApiConnected ? 'bg-slate-800/50 border-slate-700' : 'bg-rose-900/10 border-rose-800/50'}`}>
            <div className={`w-2 h-2 rounded-full animate-pulse shadow-lg ${isApiConnected ? 'bg-green-500 shadow-green-500/50' : 'bg-rose-500 shadow-rose-500/50'}`} />
            <div>
              <div className={`text-sm font-medium ${isApiConnected ? 'text-slate-200' : 'text-rose-400'}`}>
                {isApiConnected ? 'API Servisi Çalışıyor' : 'API Bağlantısı Yok'}
              </div>
              <div className={`text-xs truncate max-w-[150px] ${isApiConnected ? 'text-slate-400' : 'text-rose-500/70'}`} title={API_BASE_URL}>
                {isApiConnected ? API_BASE_URL : 'Yeniden bağlanılıyor...'}
              </div>
            </div>
          </div>

          {ipcRenderer && (
            <button
              onClick={() => { ipcRenderer.send('updater:check'); setShowUpdateBar(true); setUpdateStatus('checking'); }}
              className="w-full text-xs py-1.5 px-2 rounded bg-slate-700/50 hover:bg-slate-700 text-slate-400 hover:text-slate-200 transition-colors text-left flex items-center space-x-2"
            >
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"/></svg>
              <span>Güncellemeleri Kontrol Et</span>
            </button>
          )}
        </div>
      </aside>

      {/* Main Content */}
      <main className="flex-1 flex flex-col min-w-0 overflow-hidden bg-[#0a0f1c] relative">

        {/* Güncelleme Bildirimi */}
        {showUpdateBar && (
          <div className={`px-6 py-2.5 flex items-center justify-between text-sm border-b transition-colors ${
            updateStatus === 'downloaded' ? 'bg-emerald-900/40 border-emerald-700/50 text-emerald-300' :
            updateStatus === 'error'      ? 'bg-rose-900/30 border-rose-700/50 text-rose-300' :
                                           'bg-blue-900/30 border-blue-700/50 text-blue-300'
          }`}>
            <div className="flex items-center space-x-3">
              {updateStatus === 'checking' && (
                <><svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24"><circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"/><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z"/></svg><span>Güncellemeler kontrol ediliyor...</span></>
              )}
              {updateStatus === 'available' && (
                <><span className="w-2 h-2 rounded-full bg-blue-400 animate-pulse inline-block"/><span>v{updateVersion} mevcut — indiriliyor...</span></>
              )}
              {updateStatus === 'latest' && (
                <><span className="w-2 h-2 rounded-full bg-emerald-400 inline-block"/><span>Uygulama güncel.</span></>
              )}
              {updateStatus === 'downloaded' && (
                <><span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse inline-block"/><span>v{updateVersion} indirildi — yeniden başlatmaya hazır.</span></>
              )}
              {updateStatus === 'error' && (
                <><span className="w-2 h-2 rounded-full bg-rose-400 inline-block"/><span>Güncelleme hatası.</span></>
              )}
              {updateProgress && updateStatus === 'available' && (
                <div className="flex items-center space-x-2 ml-2">
                  <div className="w-32 h-1.5 bg-blue-900 rounded-full overflow-hidden">
                    <div className="h-full bg-blue-400 rounded-full transition-all" style={{ width: `${updateProgress.percent}%` }} />
                  </div>
                  <span className="text-xs text-blue-400/80">{updateProgress.percent}%</span>
                </div>
              )}
            </div>
            <div className="flex items-center space-x-3">
              {updateStatus === 'latest' || updateStatus === 'error' ? (
                <button onClick={() => ipcRenderer?.send('updater:check')} className="text-xs underline opacity-70 hover:opacity-100">Tekrar Kontrol Et</button>
              ) : null}
              <button onClick={() => setShowUpdateBar(false)} className="opacity-50 hover:opacity-100 text-lg leading-none">×</button>
            </div>
          </div>
        )}
        <header className="h-16 border-b border-slate-800 flex items-center justify-between px-8 bg-slate-900/50 backdrop-blur-md z-10">
          <h1 className="text-xl font-semibold tracking-tight text-white">
            {activeTab === 'banned' ? 'Engellenen IP Adresleri' : activeTab === 'whitelist' ? 'Güvenilir IP Adresleri' : 'Canlı Sistem Logları'}
          </h1>
          <div className="flex items-center space-x-4">
            {(activeTab === 'banned' || activeTab === 'whitelist') && (
              <div className="flex space-x-2">
                {activeTab === 'banned' && (
                  <select value={filterReason} onChange={handleFilterChange}
                    className="block p-2 text-sm border rounded-lg bg-slate-800 border-slate-700 text-slate-300 focus:ring-blue-500 focus:border-blue-500 focus:outline-none cursor-pointer">
                    <option value="">Tüm Sebepler</option>
                    {uniqueReasons.map((r, i) => <option key={i} value={r}>{r}</option>)}
                  </select>
                )}
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none">
                    <svg className="w-4 h-4 text-slate-500" fill="none" viewBox="0 0 20 20"><path stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="m19 19-4-4m0-7A7 7 0 1 1 1 8a7 7 0 0 1 14 0Z" /></svg>
                  </div>
                  <input type="text" value={searchTerm} onChange={handleSearchChange}
                    className="block w-64 p-2 pl-10 text-sm border rounded-lg bg-slate-800 border-slate-700 placeholder-slate-500 text-white focus:ring-blue-500 focus:border-blue-500 focus:outline-none"
                    placeholder="IP Adresi veya Sebep Ara..." />
                </div>
              </div>
            )}
            {activeTab === 'logs' && (
              <div className="flex items-center space-x-3">
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none">
                    <svg className="w-4 h-4 text-slate-500" fill="none" viewBox="0 0 20 20"><path stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="m19 19-4-4m0-7A7 7 0 1 1 1 8a7 7 0 0 1 14 0Z" /></svg>
                  </div>
                  <input type="text" value={logSearch} onChange={e => setLogSearch(e.target.value)}
                    className="block w-64 p-2 pl-10 text-sm border rounded-lg bg-slate-800 border-slate-700 placeholder-slate-500 text-white focus:ring-blue-500 focus:border-blue-500 focus:outline-none"
                    placeholder="Loglarda ara..." />
                </div>
                <button onClick={() => setAutoScroll(v => !v)}
                  className={`px-3 py-2 text-xs font-medium rounded-md border transition-colors ${autoScroll ? 'bg-blue-600/20 border-blue-500/50 text-blue-300 hover:bg-blue-600/30' : 'bg-slate-800 border-slate-700 text-slate-400 hover:bg-slate-700'}`}>
                  {autoScroll ? '⏬ Oto-Kaydırma Açık' : '⏸ Oto-Kaydırma Kapalı'}
                </button>
              </div>
            )}
            <button onClick={() => setIsModalOpen(true)}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white text-sm font-medium rounded-md shadow focus:ring-2 focus:ring-blue-500/50 transition-colors">
              {activeTab === 'whitelist' ? '+ Yeni Beyaz Liste IP Ekle' : activeTab === 'banned' ? '+ Manuel IP Ekle' : 'Ayarlar'}
            </button>
          </div>
        </header>

        <div className="flex-1 overflow-auto p-8 relative">
          {/* Tab: Banned IPs */}
          {activeTab === 'banned' && (
            <div className="space-y-6">
              <div className="grid grid-cols-4 gap-4">
                {[
                  { label: 'Toplam Ban', value: stats.totalBans, color: 'text-white' },
                  { label: 'Aktif Ban', value: stats.activeBans, color: 'text-rose-400' },
                  { label: 'Bugün Engellenen', value: stats.todayBans, color: 'text-blue-400' },
                  { label: 'AbuseIPDB Raporu', value: stats.reportedBans, color: 'text-purple-400' },
                ].map(s => (
                  <div key={s.label} className="bg-slate-800/50 border border-slate-700/50 rounded-xl p-5 shadow-sm backdrop-blur">
                    <div className="text-slate-400 text-sm font-medium mb-1">{s.label}</div>
                    <div className={`text-3xl font-bold ${s.color}`}>{s.value || 0}</div>
                  </div>
                ))}
              </div>
              <div className="bg-slate-800/50 border border-slate-700/50 rounded-xl shadow-sm backdrop-blur overflow-hidden flex flex-col">
                <table className="w-full text-left border-collapse">
                  <thead>
                    <tr className="bg-slate-800/80 border-b border-slate-700/80 text-xs uppercase tracking-wider text-slate-400">
                      <th className="px-6 py-4 font-medium">IP Adresi</th>
                      <th className="px-6 py-4 font-medium">Sebep</th>
                      <th className="px-6 py-4 font-medium">Zaman</th>
                      <th className="px-6 py-4 font-medium">Bitiş</th>
                      <th className="px-6 py-4 font-medium text-right">İşlem</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-700/50 text-sm text-slate-300">
                    {displayedBans.length === 0 ? (
                      <tr><td colSpan="5" className="px-6 py-8 text-center text-slate-500">Gösterilecek aktif kural bulunamadı.</td></tr>
                    ) : displayedBans.map(b => (
                      <tr key={b.id} className="hover:bg-slate-800/50 transition-colors group">
                        <td className="px-6 py-4 font-mono font-medium text-rose-300">{b.ipAddress}</td>
                        <td className="px-6 py-4"><span className="inline-flex items-center px-2 py-1 rounded-md text-xs font-medium bg-rose-500/10 text-rose-400 border border-rose-500/20">{b.reason}</span></td>
                        <td className="px-6 py-4 text-slate-400">{new Date(b.bannedAt).toLocaleString('tr-TR')}</td>
                        <td className="px-6 py-4 text-slate-400">{b.expiresAt ? new Date(b.expiresAt).toLocaleString('tr-TR') : 'Kalıcı'}</td>
                        <td className="px-6 py-4 text-right">
                          <div className="flex justify-end space-x-2 opacity-0 group-hover:opacity-100 transition-opacity">
                            <button onClick={() => handleAddToWhitelist(b.ipAddress)} className="px-2.5 py-1.5 bg-slate-700 hover:bg-slate-600 rounded text-xs text-white">Beyaza Al</button>
                            <button onClick={() => handeUnban(b.ipAddress)} className="px-2.5 py-1.5 bg-rose-900/40 border border-rose-800 hover:bg-rose-800/60 rounded text-xs text-rose-200">Kaldır</button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {totalPages > 1 && (
                  <div className="bg-slate-800/80 px-6 py-3 border-t border-slate-700/80 flex items-center justify-between">
                    <div className="text-xs text-slate-400">Toplam {filteredBans.length} kayıt, Sayfa {currentPage} / {totalPages}</div>
                    <div className="flex space-x-2">
                      <button onClick={() => setCurrentPage(p => Math.max(p - 1, 1))} disabled={currentPage === 1} className="px-3 py-1 bg-slate-700 hover:bg-slate-600 disabled:opacity-50 text-xs text-white rounded transition-colors">Önceki</button>
                      <button onClick={() => setCurrentPage(p => Math.min(p + 1, totalPages))} disabled={currentPage === totalPages} className="px-3 py-1 bg-slate-700 hover:bg-slate-600 disabled:opacity-50 text-xs text-white rounded transition-colors">Sonraki</button>
                    </div>
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Tab: Whitelist */}
          {activeTab === 'whitelist' && (
            <div className="space-y-6">
              <div className="bg-slate-800/50 border border-slate-700/50 rounded-xl shadow-sm backdrop-blur overflow-hidden">
                <table className="w-full text-left border-collapse">
                  <thead>
                    <tr className="bg-slate-800/80 border-b border-slate-700/80 text-xs uppercase tracking-wider text-slate-400">
                      <th className="px-6 py-4 font-medium">IP Adresi</th>
                      <th className="px-6 py-4 font-medium">Açıklama</th>
                      <th className="px-6 py-4 font-medium">Eklenme Zamanı</th>
                      <th className="px-6 py-4 font-medium text-right">İşlem</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-700/50 text-sm text-slate-300">
                    {whitelist.filter(w => w.ipAddress.includes(searchTerm) || (w.description && w.description.toLowerCase().includes(searchTerm.toLowerCase()))).length === 0 ? (
                      <tr><td colSpan="4" className="px-6 py-8 text-center text-slate-500">Beyaz liste boş veya aramanıza uygun sonuç bulunamadı.</td></tr>
                    ) : whitelist.filter(w => w.ipAddress.includes(searchTerm) || (w.description && w.description.toLowerCase().includes(searchTerm.toLowerCase()))).map(w => (
                      <tr key={w.id} className="hover:bg-slate-800/50 transition-colors group">
                        <td className="px-6 py-4 font-mono font-medium text-green-400">{w.ipAddress}</td>
                        <td className="px-6 py-4">{w.description}</td>
                        <td className="px-6 py-4 text-slate-400">{new Date(w.addedAt).toLocaleString('tr-TR')}</td>
                        <td className="px-6 py-4 text-right">
                          <div className="flex justify-end space-x-2 opacity-0 group-hover:opacity-100 transition-opacity">
                            <button onClick={() => handleRemoveWhitelist(w.ipAddress)} className="px-2.5 py-1.5 bg-rose-900/40 border border-rose-800 hover:bg-rose-800/60 rounded text-xs text-rose-200">Sil</button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Tab: Logs */}
          {activeTab === 'logs' && (
            <div className="font-mono text-sm bg-black/40 border border-slate-700 p-4 rounded-xl shadow-inner h-full flex flex-col">
              <div className="text-slate-500 border-b border-slate-800 pb-2 mb-2 flex justify-between items-center">
                <span>Canlı Api Logları / Arka Plan İstekleri...</span>
                <span className="text-xs text-slate-500">{filteredLogs.length} / {logs.length} satır</span>
              </div>
              <div className="flex-1 overflow-auto space-y-1">
                {filteredLogs.length === 0 ? (
                  <div className="text-slate-500 text-sm">{logSearch ? 'Aramanıza uygun log bulunamadı.' : 'Log bekleniyor...'}</div>
                ) : filteredLogs.map((log, index) => {
                  let color = 'text-slate-300';
                  if (log.includes('WARN') || log.includes('Warning')) color = 'text-amber-400';
                  if (log.includes('ERROR') || log.includes('Fail') || log.includes('Error')) color = 'text-rose-400';
                  if (log.includes('INFO') || log.includes('Success')) color = 'text-emerald-400';
                  return <div key={index} className={color}>{log}</div>;
                })}
                <div ref={logsEndRef} />
              </div>
            </div>
          )}
        </div>
      </main>

      {/* IP BAN / WHITELIST MODAL */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
          <div className="bg-slate-800 border border-slate-700 rounded-xl p-6 w-96 shadow-2xl">
            <h3 className="text-lg font-bold text-white mb-4">
              {activeTab === 'whitelist' ? 'Güvenilir IP Ekle' : 'Manuel IP Engelle'}
            </h3>
            <div className="space-y-4">
              <div>
                <label className={labelCls}>IP Adresi</label>
                <input value={modalIp} onChange={e => setModalIp(e.target.value)} type="text" className={inputCls} placeholder="Örn: 192.168.1.10 veya 192.168.0.0/24 (CIDR)" />
              </div>
              <div>
                <label className={labelCls}>Açıklama / Sebep</label>
                <input value={modalReason} onChange={e => setModalReason(e.target.value)} type="text" className={inputCls} placeholder={activeTab === 'whitelist' ? 'Örn: Ofis Ağı' : 'Örn: Manuel Risk'} />
              </div>
            </div>
            <div className="mt-6 flex justify-end space-x-3">
              <button onClick={() => setIsModalOpen(false)} className="px-4 py-2 bg-transparent text-slate-300 hover:text-white transition-colors">İptal</button>
              <button onClick={handleManualAdd} className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white font-medium rounded-md shadow transition-colors">Kaydet</button>
            </div>
          </div>
        </div>
      )}

      {/* SUNUCU EKLE / DÜZENLE MODAL */}
      {isServerModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm">
          <div className="bg-slate-800 border border-slate-700 rounded-xl p-6 w-[480px] shadow-2xl">
            <h3 className="text-lg font-bold text-white mb-5">
              {isEditMode ? '✏️ Sunucuyu Düzenle' : '+ Yeni Sunucu Bağla'}
            </h3>

            <div className="space-y-4">
              {/* Temel Bilgiler */}
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className={labelCls}>Sunucu Adı</label>
                  <input value={serverForm.name} onChange={e => setServerForm(f => ({ ...f, name: e.target.value }))} type="text" className={inputCls} placeholder="Örn: İstanbul Sunucu" />
                </div>
                <div>
                  <label className={labelCls}>API Adresi</label>
                  <input value={serverForm.url} onChange={e => setServerForm(f => ({ ...f, url: e.target.value }))} type="text" className={inputCls} placeholder="http://192.168.1.50:5009/api" />
                </div>
              </div>

              {/* Proxy Bölümü */}
              <div className="border border-slate-700 rounded-lg p-4 space-y-3">
                <div className="text-xs font-semibold text-slate-400 uppercase tracking-wider mb-1">Proxy Ayarları <span className="text-slate-600 normal-case font-normal">(opsiyonel)</span></div>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className={labelCls}>Proxy Host / IP</label>
                    <input value={serverForm.proxy.host} onChange={e => setServerForm(f => ({ ...f, proxy: { ...f.proxy, host: e.target.value } }))} type="text" className={inputCls} placeholder="Örn: 10.0.0.5" />
                  </div>
                  <div>
                    <label className={labelCls}>Port</label>
                    <input value={serverForm.proxy.port} onChange={e => setServerForm(f => ({ ...f, proxy: { ...f.proxy, port: e.target.value } }))} type="text" className={inputCls} placeholder="Örn: 8080" />
                  </div>
                  <div>
                    <label className={labelCls}>Kullanıcı Adı</label>
                    <input value={serverForm.proxy.user} onChange={e => setServerForm(f => ({ ...f, proxy: { ...f.proxy, user: e.target.value } }))} type="text" className={inputCls} placeholder="Opsiyonel" />
                  </div>
                  <div>
                    <label className={labelCls}>Şifre</label>
                    <input value={serverForm.proxy.pass} onChange={e => setServerForm(f => ({ ...f, proxy: { ...f.proxy, pass: e.target.value } }))} type="password" className={inputCls} placeholder="Opsiyonel" />
                  </div>
                </div>
                {serverForm.proxy.host && (
                  <div className="text-xs text-slate-500 bg-slate-900/50 rounded px-2 py-1 font-mono">
                    {serverForm.proxy.user ? `${serverForm.proxy.user}:***@` : ''}{serverForm.proxy.host}{serverForm.proxy.port ? `:${serverForm.proxy.port}` : ''}
                  </div>
                )}
              </div>
            </div>

            <div className="mt-6 flex items-center justify-between">
              <div>
                {isEditMode && servers.length > 1 && (
                  <button onClick={handleDeleteServer} className="px-3 py-2 text-xs bg-rose-900/40 border border-rose-800 hover:bg-rose-800/60 text-rose-300 rounded-md transition-colors">
                    🗑 Sunucuyu Sil
                  </button>
                )}
              </div>
              <div className="flex space-x-3">
                <button onClick={closeServerModal} className="px-4 py-2 bg-transparent text-slate-300 hover:text-white transition-colors text-sm">İptal</button>
                <button onClick={handleSaveServer} disabled={!serverForm.name || !serverForm.url}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed text-white font-medium rounded-md shadow transition-colors text-sm">
                  {isEditMode ? 'Güncelle' : 'Ekle'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}

export default App;
