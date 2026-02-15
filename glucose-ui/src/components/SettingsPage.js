import React, { useState, useEffect } from 'react';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

const REGIONS = [
  { value: 'eu', label: 'Europe (EU)' },
  { value: 'us', label: 'United States (US)' },
  { value: 'eu2', label: 'Europe 2 (EU2)' },
  { value: 'de', label: 'Germany (DE)' },
  { value: 'fr', label: 'France (FR)' },
  { value: 'jp', label: 'Japan (JP)' },
  { value: 'ap', label: 'Asia Pacific (AP)' },
  { value: 'au', label: 'Australia (AU)' },
  { value: 'ae', label: 'UAE (AE)' },
  { value: 'ca', label: 'Canada (CA)' },
];

function SettingsPage() {
  const [settings, setSettings] = useState({
    email: '',
    password: '',
    patientId: '',
    region: 'eu',
    version: '4.12.0',
    fetchIntervalMinutes: 5,
  });
  const [analysisSettings, setAnalysisSettings] = useState({
    gptApiKey: '',
    notesFolderName: 'Cukier',
    analysisIntervalMinutes: 15,
    reanalysisMinIntervalMinutes: 30,
  });
  const [isConfigured, setIsConfigured] = useState(false);
  const [isAnalysisConfigured, setIsAnalysisConfigured] = useState(false);
  const [backupStatus, setBackupStatus] = useState(null);
  const [backingUp, setBackingUp] = useState(false);
  const [backupMessage, setBackupMessage] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [savingAnalysis, setSavingAnalysis] = useState(false);
  const [testing, setTesting] = useState(false);
  const [message, setMessage] = useState(null);
  const [analysisMessage, setAnalysisMessage] = useState(null);
  const [testResult, setTestResult] = useState(null);

  useEffect(() => {
    fetchSettings();
  }, []);

  const fetchSettings = async () => {
    try {
      const [settingsRes, analysisRes, backupRes] = await Promise.all([
        fetch(`${API_BASE}/settings`),
        fetch(`${API_BASE}/settings/analysis`),
        fetch(`${API_BASE}/settings/backup`),
      ]);
      if (settingsRes.ok) {
        const data = await settingsRes.json();
        setSettings(data);
        setIsConfigured(data.isConfigured);
      }
      if (analysisRes.ok) {
        const data = await analysisRes.json();
        setAnalysisSettings(data);
        setIsAnalysisConfigured(data.isConfigured);
      }
      if (backupRes.ok) {
        const data = await backupRes.json();
        setBackupStatus(data);
      }
    } catch (err) {
      setMessage({ type: 'error', text: 'Failed to load settings.' });
    } finally {
      setLoading(false);
    }
  };

  const [restoring, setRestoring] = useState(null); // fileName being restored
  const [confirmRestore, setConfirmRestore] = useState(null); // fileName pending confirmation

  const handleTriggerBackup = async () => {
    setBackingUp(true);
    setBackupMessage(null);
    try {
      const res = await fetch(`${API_BASE}/settings/backup`, { method: 'POST' });
      const data = await res.json();
      setBackupMessage({ type: res.ok ? 'success' : 'error', text: data.message });
      // Refresh backup status
      const statusRes = await fetch(`${API_BASE}/settings/backup`);
      if (statusRes.ok) setBackupStatus(await statusRes.json());
    } catch (err) {
      setBackupMessage({ type: 'error', text: 'Failed to trigger backup.' });
    } finally {
      setBackingUp(false);
    }
  };

  const handleRestore = async (fileName) => {
    setConfirmRestore(null);
    setRestoring(fileName);
    setBackupMessage(null);
    try {
      const res = await fetch(`${API_BASE}/settings/backup/restore`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fileName }),
      });
      const data = await res.json();
      setBackupMessage({ type: res.ok ? 'success' : 'error', text: data.message });
    } catch (err) {
      setBackupMessage({ type: 'error', text: 'Failed to restore from backup.' });
    } finally {
      setRestoring(null);
    }
  };

  const handleChange = (e) => {
    const { name, value } = e.target;
    setSettings(prev => ({
      ...prev,
      [name]: name === 'fetchIntervalMinutes' ? parseInt(value) || 1 : value
    }));
  };

  const handleSave = async (e) => {
    e.preventDefault();
    setSaving(true);
    setMessage(null);

    try {
      const res = await fetch(`${API_BASE}/settings`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings),
      });

      if (res.ok) {
        setMessage({ type: 'success', text: 'Settings saved successfully! Data fetching will start/restart shortly.' });
        setIsConfigured(true);
      } else {
        const data = await res.json();
        setMessage({ type: 'error', text: data.message || 'Failed to save settings.' });
      }
    } catch (err) {
      setMessage({ type: 'error', text: 'Failed to save settings.' });
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    setTesting(true);
    setTestResult(null);

    try {
      const res = await fetch(`${API_BASE}/settings/test`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings),
      });

      if (res.ok) {
        const data = await res.json();
        setTestResult(data);
      } else {
        setTestResult({ success: false, message: 'Request failed.' });
      }
    } catch (err) {
      setTestResult({ success: false, message: 'Could not connect to API.' });
    } finally {
      setTesting(false);
    }
  };

  const handleAnalysisChange = (e) => {
    const { name, value } = e.target;
    const intFields = ['analysisIntervalMinutes', 'reanalysisMinIntervalMinutes'];
    setAnalysisSettings(prev => ({
      ...prev,
      [name]: intFields.includes(name) ? parseInt(value) || 1 : value
    }));
  };

  const handleSaveAnalysis = async (e) => {
    e.preventDefault();
    setSavingAnalysis(true);
    setAnalysisMessage(null);

    try {
      const res = await fetch(`${API_BASE}/settings/analysis`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(analysisSettings),
      });

      if (res.ok) {
        setAnalysisMessage({ type: 'success', text: 'Analysis settings saved successfully!' });
        setIsAnalysisConfigured(true);
      } else {
        const data = await res.json();
        setAnalysisMessage({ type: 'error', text: data.message || 'Failed to save analysis settings.' });
      }
    } catch (err) {
      setAnalysisMessage({ type: 'error', text: 'Failed to save analysis settings.' });
    } finally {
      setSavingAnalysis(false);
    }
  };

  if (loading) {
    return (
      <div className="settings-page">
        <div className="loading">
          <div className="spinner" />
          <p>Loading settings...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="settings-page">
      <div className="settings-card">
        <div className="settings-header">
          <h2>LibreLink Up Configuration</h2>
          <p>Enter your LibreLink Up credentials to start fetching glucose data.</p>
          {isConfigured && (
            <span className="config-badge configured">Configured</span>
          )}
          {!isConfigured && (
            <span className="config-badge not-configured">Not Configured</span>
          )}
        </div>

        <form onSubmit={handleSave}>
          <div className="form-section">
            <h3>Credentials</h3>

            <div className="form-group">
              <label htmlFor="email">Email</label>
              <input
                type="email"
                id="email"
                name="email"
                value={settings.email}
                onChange={handleChange}
                placeholder="your-email@example.com"
                required
              />
            </div>

            <div className="form-group">
              <label htmlFor="password">Password</label>
              <input
                type="password"
                id="password"
                name="password"
                value={settings.password}
                onChange={handleChange}
                placeholder="Your LibreLink Up password"
                required
              />
            </div>
          </div>

          <div className="form-section">
            <h3>Connection</h3>

            <div className="form-row">
              <div className="form-group">
                <label htmlFor="region">Region</label>
                <select
                  id="region"
                  name="region"
                  value={settings.region}
                  onChange={handleChange}
                >
                  {REGIONS.map(r => (
                    <option key={r.value} value={r.value}>{r.label}</option>
                  ))}
                </select>
              </div>

              <div className="form-group">
                <label htmlFor="fetchIntervalMinutes">Fetch Interval (min)</label>
                <input
                  type="number"
                  id="fetchIntervalMinutes"
                  name="fetchIntervalMinutes"
                  value={settings.fetchIntervalMinutes}
                  onChange={handleChange}
                  min="1"
                  max="60"
                />
              </div>
            </div>

            <div className="form-group">
              <label htmlFor="patientId">Patient ID <span className="optional">(optional — auto-detected)</span></label>
              <input
                type="text"
                id="patientId"
                name="patientId"
                value={settings.patientId}
                onChange={handleChange}
                placeholder="Leave empty to auto-detect"
              />
            </div>

            <div className="form-group">
              <label htmlFor="version">LLU Version <span className="optional">(advanced)</span></label>
              <input
                type="text"
                id="version"
                name="version"
                value={settings.version}
                onChange={handleChange}
              />
            </div>
          </div>

          {message && (
            <div className={`message ${message.type}`}>
              {message.text}
            </div>
          )}

          {testResult && (
            <div className={`message ${testResult.success ? 'success' : 'error'}`}>
              <strong>{testResult.success ? '✓' : '✗'} {testResult.message}</strong>
              {testResult.patients && testResult.patients.length > 0 && (
                <div className="patients-list">
                  <p>Patients found:</p>
                  <ul>
                    {testResult.patients.map((p, i) => (
                      <li key={i}>
                        {p.firstName} {p.lastName}
                        <code>{p.patientId}</code>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}

          <div className="form-actions">
            <button type="button" className="btn-test" onClick={handleTest} disabled={testing}>
              {testing ? 'Testing...' : 'Test Connection'}
            </button>
            <button type="submit" className="btn-save" disabled={saving}>
              {saving ? 'Saving...' : 'Save Settings'}
            </button>
          </div>
        </form>
      </div>

      {/* ── Analysis / GPT Settings ────────────────────── */}
      <div className="settings-card" style={{ marginTop: 24 }}>
        <div className="settings-header">
          <h2>🤖 AI Analysis Configuration</h2>
          <p>Configure GPT API key to enable AI-powered glucose response analysis.</p>
          {isAnalysisConfigured && (
            <span className="config-badge configured">Configured</span>
          )}
          {!isAnalysisConfigured && (
            <span className="config-badge not-configured">Not Configured</span>
          )}
        </div>

        <form onSubmit={handleSaveAnalysis}>
          <div className="form-section">
            <h3>OpenAI API</h3>
            <div className="form-group">
              <label htmlFor="gptApiKey">GPT API Key</label>
              <input
                type="password"
                id="gptApiKey"
                name="gptApiKey"
                value={analysisSettings.gptApiKey}
                onChange={handleAnalysisChange}
                placeholder="sk-..."
              />
            </div>
            <div className="form-group">
              <label htmlFor="gptModelName">AI Model</label>
              <select
                id="gptModelName"
                name="gptModelName"
                value={analysisSettings.gptModelName || 'gpt-4o-mini'}
                onChange={handleAnalysisChange}
              >
                <optgroup label="GPT-5.x (Newest)">
                  <option value="gpt-5.2">GPT-5.2 — $1.75 / $14.00 per 1M tokens (flagship)</option>
                  <option value="gpt-5-mini">GPT-5 Mini — $0.30 / $1.00 per 1M tokens</option>
                </optgroup>
                <optgroup label="GPT-4.1">
                  <option value="gpt-4.1">GPT-4.1 — $2.00 / $8.00 per 1M tokens (1M ctx)</option>
                  <option value="gpt-4.1-mini">GPT-4.1 Mini — $0.40 / $1.60 per 1M tokens</option>
                  <option value="gpt-4.1-nano">GPT-4.1 Nano — $0.10 / $0.40 per 1M tokens</option>
                </optgroup>
                <optgroup label="Reasoning (o-series)">
                  <option value="o4-mini">o4-mini — $1.10 / $4.40 per 1M tokens</option>
                  <option value="o3-mini">o3-mini — $1.10 / $4.40 per 1M tokens</option>
                </optgroup>
                <optgroup label="GPT-4o">
                  <option value="gpt-4o">GPT-4o — $2.50 / $10.00 per 1M tokens</option>
                  <option value="gpt-4o-mini">GPT-4o Mini — $0.15 / $0.60 per 1M tokens</option>
                </optgroup>
                <optgroup label="Legacy">
                  <option value="gpt-4-turbo">GPT-4 Turbo — $10.00 / $30.00 per 1M tokens</option>
                  <option value="gpt-3.5-turbo">GPT-3.5 Turbo — $0.50 / $1.50 per 1M tokens</option>
                </optgroup>
              </select>
              <span className="form-hint">Model used for all AI analyses. Cost shown as input / output per 1M tokens.</span>
            </div>
          </div>

          <div className="form-section">
            <h3>Event Correlation</h3>
            <div className="form-row">
              <div className="form-group">
                <label htmlFor="notesFolderName">Notes Folder Name</label>
                <input
                  type="text"
                  id="notesFolderName"
                  name="notesFolderName"
                  value={analysisSettings.notesFolderName}
                  onChange={handleAnalysisChange}
                  placeholder="Cukier"
                />
                <span className="form-hint">Samsung Notes folder to correlate with glucose data</span>
              </div>

              <div className="form-group">
                <label htmlFor="analysisIntervalMinutes">Analysis Interval (min)</label>
                <input
                  type="number"
                  id="analysisIntervalMinutes"
                  name="analysisIntervalMinutes"
                  value={analysisSettings.analysisIntervalMinutes}
                  onChange={handleAnalysisChange}
                  min="1"
                  max="120"
                />
                <span className="form-hint">How often the background worker checks for new events</span>
              </div>

              <div className="form-group">
                <label htmlFor="reanalysisMinIntervalMinutes">Re-analysis Cooldown (min)</label>
                <input
                  type="number"
                  id="reanalysisMinIntervalMinutes"
                  name="reanalysisMinIntervalMinutes"
                  value={analysisSettings.reanalysisMinIntervalMinutes}
                  onChange={handleAnalysisChange}
                  min="1"
                  max="1440"
                />
                <span className="form-hint">Min. time between AI re-analyses triggered by new glucose data. New events always analyze immediately.</span>
              </div>
            </div>

            <div className="form-group">
              <label htmlFor="timeZone">Display Timezone</label>
              <select
                id="timeZone"
                name="timeZone"
                value={analysisSettings.timeZone || 'Europe/Warsaw'}
                onChange={handleAnalysisChange}
              >
                <option value="Europe/Warsaw">Europe/Warsaw (CET/CEST)</option>
                <option value="Europe/London">Europe/London (GMT/BST)</option>
                <option value="Europe/Berlin">Europe/Berlin (CET/CEST)</option>
                <option value="Europe/Paris">Europe/Paris (CET/CEST)</option>
                <option value="America/New_York">America/New_York (EST/EDT)</option>
                <option value="America/Chicago">America/Chicago (CST/CDT)</option>
                <option value="America/Denver">America/Denver (MST/MDT)</option>
                <option value="America/Los_Angeles">America/Los_Angeles (PST/PDT)</option>
                <option value="Asia/Tokyo">Asia/Tokyo (JST)</option>
                <option value="Australia/Sydney">Australia/Sydney (AEST/AEDT)</option>
                <option value="UTC">UTC</option>
              </select>
              <span className="form-hint">Timezone used for AI analysis timestamps — should match your local time</span>
            </div>
          </div>

          {analysisMessage && (
            <div className={`message ${analysisMessage.type}`}>
              {analysisMessage.text}
            </div>
          )}

          <div className="form-actions">
            <button type="submit" className="btn-save" disabled={savingAnalysis}>
              {savingAnalysis ? 'Saving...' : 'Save Analysis Settings'}
            </button>
          </div>
        </form>
      </div>
      {/* ── Database Backup ────────────────────────────── */}
      <div className="settings-card" style={{ marginTop: 24 }}>
        <div className="settings-header">
          <h2>💾 Database Backup</h2>
          <p>Full SQL Server database backups are created automatically once per day. Retained for 7 days.</p>
        </div>

        <div className="form-section">
          <h3>Backup Status</h3>
          {backupStatus ? (
            <div className="backup-status-grid">
              <div className="backup-stat">
                <span className="backup-stat-label">Last Backup</span>
                <span className="backup-stat-value">
                  {backupStatus.lastBackupUtc
                    ? new Date(backupStatus.lastBackupUtc).toLocaleString()
                    : 'Never'}
                </span>
              </div>
              <div className="backup-stat">
                <span className="backup-stat-label">File</span>
                <span className="backup-stat-value" style={{ fontSize: '0.8rem' }}>
                  {backupStatus.lastBackupFile || '—'}
                </span>
              </div>
              <div className="backup-stat">
                <span className="backup-stat-label">Size</span>
                <span className="backup-stat-value">
                  {backupStatus.lastBackupSizeBytes != null
                    ? formatBytes(backupStatus.lastBackupSizeBytes)
                    : '—'}
                </span>
              </div>
              <div className="backup-stat">
                <span className="backup-stat-label">Backups Stored</span>
                <span className="backup-stat-value">
                  {backupStatus.backupCount} ({formatBytes(backupStatus.totalSizeBytes)})
                </span>
              </div>
              {backupStatus.lastError && (
                <div className="backup-stat" style={{ gridColumn: '1 / -1' }}>
                  <span className="backup-stat-label" style={{ color: 'var(--red)' }}>Last Error</span>
                  <span className="backup-stat-value" style={{ color: 'var(--red)', fontSize: '0.8rem' }}>
                    {backupStatus.lastError}
                  </span>
                </div>
              )}
            </div>
          ) : (
            <p style={{ color: 'var(--text-muted)', fontSize: '0.88rem' }}>Loading backup status...</p>
          )}

          {backupStatus?.backups?.length > 0 && (
            <div className="backup-file-list">
              <h4 style={{ fontSize: '0.85rem', color: 'var(--text-muted)', margin: '16px 0 8px' }}>Stored Backups</h4>
              {backupStatus.backups.map((b) => (
                <div key={b.fileName} className="backup-file-item">
                  <span className="backup-file-name">{b.fileName}</span>
                  <span className="backup-file-size">{formatBytes(b.size)}</span>
                  <span className="backup-file-date">{new Date(b.createdUtc).toLocaleString()}</span>
                  <button
                    className="btn-restore"
                    onClick={() => setConfirmRestore(b.fileName)}
                    disabled={!!restoring || backingUp}
                    title="Restore database from this backup"
                  >
                    {restoring === b.fileName ? '⏳' : '↩'}
                  </button>
                </div>
              ))}
            </div>
          )}

          {/* Restore confirmation dialog */}
          {confirmRestore && (
            <div className="restore-confirm-overlay" onClick={() => setConfirmRestore(null)}>
              <div className="restore-confirm-dialog" onClick={(e) => e.stopPropagation()}>
                <h3>⚠️ Restore Database</h3>
                <p>
                  Are you sure you want to restore the database from<br />
                  <strong>{confirmRestore}</strong>?
                </p>
                <p className="restore-warning">
                  This will <strong>replace all current data</strong> with the data from this backup.
                  This action cannot be undone.
                </p>
                <div className="restore-confirm-actions">
                  <button className="btn-cancel" onClick={() => setConfirmRestore(null)}>Cancel</button>
                  <button className="btn-danger" onClick={() => handleRestore(confirmRestore)}>
                    Restore Database
                  </button>
                </div>
              </div>
            </div>
          )}
        </div>

        {backupMessage && (
          <div className={`message ${backupMessage.type}`}>
            {backupMessage.text}
          </div>
        )}

        <div className="form-actions">
          <button
            type="button"
            className="btn-save"
            onClick={handleTriggerBackup}
            disabled={backingUp || backupStatus?.isRunning}
          >
            {backingUp || backupStatus?.isRunning ? '⏳ Backing up...' : '💾 Backup Now'}
          </button>
        </div>
      </div>
    </div>
  );
}

function formatBytes(bytes) {
  if (bytes == null || bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

export default SettingsPage;
