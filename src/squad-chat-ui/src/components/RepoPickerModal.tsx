import { useCallback, useEffect, useState } from 'react'
import { getConfig, setConfig } from '../api'

interface RepoPickerModalProps {
  onRepoSelected: (repo: string) => void
}

export function RepoPickerModal({ onRepoSelected }: RepoPickerModalProps) {
  const [repoInput, setRepoInput] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const [isSaving, setIsSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Check if a target repo is already configured
  useEffect(() => {
    getConfig('target-repo')
      .then((value) => {
        if (value) {
          onRepoSelected(value)
        }
      })
      .catch(() => {
        // Config API not available yet — show picker
      })
      .finally(() => setIsLoading(false))
  }, [onRepoSelected])

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault()
      const repo = repoInput.trim()

      if (!repo) {
        setError('Please enter a repository name (e.g., owner/repo)')
        return
      }

      if (!repo.includes('/')) {
        setError('Format should be owner/repo (e.g., bradygaster/my-project)')
        return
      }

      setIsSaving(true)
      setError(null)

      try {
        await setConfig('target-repo', repo)
        onRepoSelected(repo)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to save configuration')
      } finally {
        setIsSaving(false)
      }
    },
    [repoInput, onRepoSelected],
  )

  if (isLoading) {
    return (
      <div className="repo-picker-overlay">
        <div className="repo-picker-modal">
          <p className="repo-picker-loading">Loading configuration...</p>
        </div>
      </div>
    )
  }

  return (
    <div className="repo-picker-overlay">
      <div className="repo-picker-modal">
        <h2>Welcome to Squad</h2>
        <p className="repo-picker-description">
          Choose a GitHub repository for your team to work on. All squads will create issues,
          PRs, and branches in this repo.
        </p>

        <form onSubmit={handleSubmit}>
          <label htmlFor="repo-input" className="repo-picker-label">
            Target repository
          </label>
          <input
            id="repo-input"
            type="text"
            className="repo-picker-input"
            placeholder="owner/repo"
            value={repoInput}
            onChange={(e) => setRepoInput(e.target.value)}
            disabled={isSaving}
            autoFocus
          />

          {error && <p className="repo-picker-error">{error}</p>}

          <div className="repo-picker-actions">
            <button type="submit" className="repo-picker-button" disabled={isSaving}>
              {isSaving ? 'Saving...' : 'Set target repo'}
            </button>
          </div>
        </form>

        <p className="repo-picker-hint">
          You can switch to a different repo any time via the &ldquo;Change repo&rdquo; button in the header.
        </p>
      </div>
    </div>
  )
}
