import React, { useState, useEffect, useCallback, useRef } from 'react';
import { format, parseISO } from 'date-fns';

const API_BASE = process.env.REACT_APP_API_URL || '/api';

function NotesPage() {
  const [notes, setNotes] = useState([]);
  const [folders, setFolders] = useState([]);
  const [selectedFolder, setSelectedFolder] = useState('');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedNote, setSelectedNote] = useState(null);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(true);
  const modalRef = useRef(null);

  const fetchNotes = useCallback(async () => {
    try {
      const params = new URLSearchParams();
      if (selectedFolder) params.set('folder', selectedFolder);
      if (searchQuery) params.set('search', searchQuery);

      const res = await fetch(`${API_BASE}/notes?${params}`);
      if (res.ok) {
        const data = await res.json();
        setNotes(data);
      }
    } catch (err) {
      console.error('Failed to fetch notes:', err);
    } finally {
      setLoading(false);
    }
  }, [selectedFolder, searchQuery]);

  const fetchFolders = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/notes/folders`);
      if (res.ok) {
        const data = await res.json();
        setFolders(data);
      }
    } catch (err) {
      console.error('Failed to fetch folders:', err);
    }
  }, []);

  const fetchStatus = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/notes/status`);
      if (res.ok) {
        const data = await res.json();
        setStatus(data);
      }
    } catch (err) {
      console.error('Failed to fetch notes status:', err);
    }
  }, []);

  useEffect(() => {
    fetchNotes();
    fetchFolders();
    fetchStatus();
  }, [fetchNotes, fetchFolders, fetchStatus]);

  // Close modal on Escape key or clicking outside
  useEffect(() => {
    if (!selectedNote) return;

    const handleKeyDown = (e) => {
      if (e.key === 'Escape') setSelectedNote(null);
    };

    const handleClickOutside = (e) => {
      if (modalRef.current && !modalRef.current.contains(e.target)) {
        setSelectedNote(null);
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    document.addEventListener('mousedown', handleClickOutside);
    // Prevent body scrolling while modal is open
    document.body.style.overflow = 'hidden';

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      document.removeEventListener('mousedown', handleClickOutside);
      document.body.style.overflow = '';
    };
  }, [selectedNote]);

  const truncateText = (text, maxLength = 150) => {
    if (!text || text.length <= maxLength) return text;
    return text.substring(0, maxLength) + '...';
  };

  return (
    <div className="notes-page">
      {/* Status banner */}
      {status && !status.isAvailable && (
        <div className="notes-status-banner">
          <div className="notes-status-icon">📱</div>
          <h3>Samsung Notes Not Connected</h3>
          <p>
            To sync your Samsung Notes, mount the Samsung Notes LocalState
            directory in Docker. Set the <code>SAMSUNG_NOTES_PATH</code> environment
            variable to your Samsung Notes data path.
          </p>
          <div className="notes-path-hint">
            <code>
              PowerShell: dir "$env:LOCALAPPDATA\Packages\SAMSUNGELECTRONICSCoLtd.SamsungNotes_*\LocalState"
            </code>
          </div>
        </div>
      )}

      {/* Controls */}
      <div className="notes-controls">
        <div className="notes-search">
          <input
            type="text"
            placeholder="Search notes..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>
        {folders.length > 0 && (
          <div className="notes-folder-filter">
            <select
              value={selectedFolder}
              onChange={(e) => setSelectedFolder(e.target.value)}
            >
              <option value="">All folders</option>
              {folders.map((f) => (
                <option key={f} value={f}>{f}</option>
              ))}
            </select>
          </div>
        )}
        {status && (
          <div className="notes-count">
            {status.noteCount} note{status.noteCount !== 1 ? 's' : ''}
          </div>
        )}
      </div>

      {/* Notes list */}
      {loading ? (
        <div className="loading">
          <div className="spinner" />
          <p>Loading notes...</p>
        </div>
      ) : notes.length === 0 ? (
        <div className="notes-empty">
          <div className="notes-empty-icon">📝</div>
          <p>
            {searchQuery || selectedFolder
              ? 'No notes match your search.'
              : status?.isAvailable
                ? 'No notes synced yet. Waiting for the next sync cycle...'
                : 'Connect Samsung Notes to see your notes here.'}
          </p>
        </div>
      ) : (
        <div className="notes-list">
          {notes.map((note) => (
            <div
              key={note.id}
              className="note-card"
              onClick={() => setSelectedNote(note)}
            >
              <div className="note-card-header">
                <div className="note-title-row">
                  <h3 className="note-title">{note.title || 'Untitled'}</h3>
                  <div className="note-badges">
                    {note.hasMedia && <span className="note-badge media" title="Has media">🖼</span>}
                    {note.folderName && (
                      <span className="note-badge folder">{note.folderName}</span>
                    )}
                  </div>
                </div>
                <div className="note-meta">
                  <span className="note-date">
                    {format(parseISO(note.modifiedAt), 'MMM dd, yyyy HH:mm')}
                  </span>
                </div>
              </div>

              {note.textContent && (
                <div className="note-preview">
                  {truncateText(note.textContent)}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Note Detail Modal */}
      {selectedNote && (
        <div className="note-modal-overlay">
          <div className="note-modal" ref={modalRef}>
            <div className="note-modal-header">
              <div>
                <h2 className="note-modal-title">{selectedNote.title || 'Untitled'}</h2>
                <div className="note-modal-meta">
                  <span className="note-date">
                    {format(parseISO(selectedNote.modifiedAt), 'EEEE, MMM dd, yyyy \'at\' HH:mm')}
                  </span>
                  {selectedNote.folderName && (
                    <span className="note-badge folder">{selectedNote.folderName}</span>
                  )}
                  {selectedNote.hasMedia && (
                    <span className="note-badge media" title="Has media">🖼 Media</span>
                  )}
                </div>
              </div>
              <button className="note-modal-close" onClick={() => setSelectedNote(null)} title="Close (Esc)">
                ✕
              </button>
            </div>

            <div className="note-modal-body">
              {selectedNote.textContent ? (
                <div className="note-modal-content">{selectedNote.textContent}</div>
              ) : (
                <div className="note-modal-empty">No text content available for this note.</div>
              )}

              {selectedNote.hasPreview && (
                <div className="note-modal-preview">
                  <img
                    src={`${API_BASE}/notes/${selectedNote.id}/preview`}
                    alt="Note preview"
                    loading="lazy"
                  />
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default NotesPage;
